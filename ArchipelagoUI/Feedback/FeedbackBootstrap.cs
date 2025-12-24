using BepInEx.Logging;
using OriBFArchipelago.MapTracker.Core;
using OriModding.BF.Core;
using OriModding.BF.UiLib.Menu;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OriBFArchipelago.ArchipelagoUI.Feedback
{
    internal class FeedbackBootstrap
    {
        private static GameObject feedbackSenderObject;
        private static FeedbackSender feedbackSender;

        internal static void Init()
        {
            SceneBootstrap.RegisterHandler(Bootstrap);
        }

        private static void Bootstrap(SceneBootstrap bootstrap)
        {
            feedbackSenderObject = new GameObject("FeedbackSender");
            feedbackSender = feedbackSenderObject.AddComponent<FeedbackSender>();
            feedbackSender.Initialize();

            // Add component to dynamically add menu item when inventory screen is detected
            var menuInjector = feedbackSenderObject.AddComponent<InventoryMenuInjector>();
            menuInjector.Initialize(feedbackSender);

            bootstrap.BootstrapActions = new Dictionary<string, Action<SceneRoot>>
            {
                ["titleScreenSwallowsNest"] = delegate (SceneRoot sceneRoot)
                {
                    CleverMenuItemSelectionManager component = ((Component)((Component)sceneRoot).transform.Find("ui/group/3. fullGameMainMenu")).GetComponent<CleverMenuItemSelectionManager>();
                    BasicMessageProvider messageProvider = ScriptableObject.CreateInstance<BasicMessageProvider>();
                    component.AddMenuItem("MOD FEEDBACK", component.MenuItems.Count - 1, delegate
                    {
                        component.enabled = false;
                        GameObject feedbackObj = new GameObject("FeedbackWindow");
                        FeedbackWindow window = feedbackObj.AddComponent<FeedbackWindow>();
                        window.Initialize((string feedback) =>
                        {
                            feedbackSender.SendFeedback(feedback);
                            ModLogger.Debug("Feedback sent: " + feedback);
                            UnityEngine.Object.Destroy(window.gameObject);
                            component.enabled = true;
                        }, () =>
                        {
                            component.enabled = true;
                        });
                    });
                }
            };
        }

        private static string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform current = obj.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }
    }

    internal class InventoryMenuInjector : MonoBehaviour
    {
        private FeedbackSender feedbackSender;
        private CleverMenuItemSelectionManager lastMenu = null;
        private Dictionary<CleverMenuItemSelectionManager, bool> menuItemAdded = new Dictionary<CleverMenuItemSelectionManager, bool>();

        public void Initialize(FeedbackSender sender)
        {
            feedbackSender = sender;
        }

        private void Update()
        {
            // Search for the inventory screen menu
            var allMenus = UnityEngine.Object.FindObjectsOfType<CleverMenuItemSelectionManager>();
            CleverMenuItemSelectionManager currentMenu = null;

            foreach (var menu in allMenus)
            {
                if (menu.name == "inventoryScreen" && menu.MenuItems != null && menu.MenuItems.Count > 0)
                {
                    currentMenu = menu;
                    break;
                }
            }

            // If we found a menu and it's different from the last one we modified, add the item
            if (currentMenu != null && currentMenu != lastMenu)
            {
                // Check if MOD FEEDBACK already exists in this menu
                bool alreadyExists = currentMenu.MenuItems.Any(m => m.name == "MOD FEEDBACK");
                if (!alreadyExists)
                {
                    ModLogger.Debug($"Found inventoryScreen menu, adding feedback button");
                    AddFeedbackMenuItem(currentMenu);
                    lastMenu = currentMenu;
                }
            }

            // Continuously check and maintain positions for the current menu
            if (currentMenu != null && menuItemAdded.ContainsKey(currentMenu) && menuItemAdded[currentMenu])
            {
                MaintainMenuPositions(currentMenu);
            }
        }

        private void AddFeedbackMenuItem(CleverMenuItemSelectionManager component)
        {
            try
            {
                ModLogger.Debug($"Menu has {component.MenuItems.Count} items before adding");

                // Log all menu item names
                for (int i = 0; i < component.MenuItems.Count; i++)
                {
                    var item = component.MenuItems[i];
                    ModLogger.Debug($"  [{i}] {item.name}");
                }

                // Find the "exit" item index to insert before it
                int exitIndex = -1;
                for (int i = 0; i < component.MenuItems.Count; i++)
                {
                    if (component.MenuItems[i].name == "exit")
                    {
                        exitIndex = i;
                        break;
                    }
                }

                int insertIndex = exitIndex >= 0 ? exitIndex : component.MenuItems.Count - 1;
                ModLogger.Debug($"Inserting MOD FEEDBACK at index {insertIndex} (before exit at {exitIndex})");

                // Clone options button as template
                CleverMenuItem templateItem = component.MenuItems.FirstOrDefault(m => m.name == "options");
                if (templateItem == null)
                {
                    templateItem = component.MenuItems[0];
                }

                CleverMenuItem menuItem = UnityEngine.Object.Instantiate(templateItem);
                menuItem.gameObject.name = "MOD FEEDBACK";
                menuItem.transform.SetParent(templateItem.transform.parent);

                // Clear all existing callbacks from the cloned item using reflection
                var pressedCallbackField = typeof(CleverMenuItem).GetField("PressedCallback",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (pressedCallbackField != null)
                {
                    ModLogger.Debug($"Found PressedCallback field (Public={pressedCallbackField.IsPublic}), clearing it");
                    pressedCallbackField.SetValue(menuItem, null);
                }
                else
                {
                    ModLogger.Debug($"Could not find PressedCallback field, trying event backing field");
                    var eventBackingField = typeof(CleverMenuItem).GetField("<PressedCallback>k__BackingField",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (eventBackingField != null)
                    {
                        ModLogger.Debug($"Found event backing field, clearing it");
                        eventBackingField.SetValue(menuItem, null);
                    }
                    else
                    {
                        ModLogger.Debug($"Could not find any PressedCallback field to clear");
                    }
                }

                // Store reference to this for coroutine
                InventoryMenuInjector injector = this;

                // Add our callback to the menu item
                Action feedbackAction = delegate
                {
                    component.IsSuspended = true;
                    injector.StartCoroutine(OpenFeedbackWindowNextFrame(component));
                };

                menuItem.PressedCallback += feedbackAction;

                // Set the text
                MessageBox messageBox = menuItem.gameObject.GetComponentInChildren<MessageBox>();
                if (messageBox != null)
                {
                    messageBox.SetMessage(new MessageDescriptor("MOD FEEDBACK"));
                }

                menuItem.ApplyColors();

                // Insert into the menu
                component.MenuItems.Insert(insertIndex, menuItem);

                // Mark that we've added the item to this menu
                menuItemAdded[component] = true;

                ModLogger.Debug($"Menu has {component.MenuItems.Count} items after adding");

                // Verify the item was added
                bool found = false;
                for (int i = 0; i < component.MenuItems.Count; i++)
                {
                    var item = component.MenuItems[i];
                    ModLogger.Debug($"  After: [{i}] {item.name}");
                    if (item.name == "MOD FEEDBACK")
                    {
                        found = true;
                        ModLogger.Debug($"  ^^^ Found MOD FEEDBACK at index {i}!");
                    }
                }

                if (found)
                {
                    ModLogger.Debug("Successfully added MOD FEEDBACK menu item");
                }
                else
                {
                    ModLogger.Error("MOD FEEDBACK was not found in menu after adding!");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to add menu item: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void MaintainMenuPositions(CleverMenuItemSelectionManager component)
        {
            // Find the menu items
            CleverMenuItem continueItem = component.MenuItems.FirstOrDefault(m => m.name == "continue");
            CleverMenuItem optionsItem = component.MenuItems.FirstOrDefault(m => m.name == "options");
            CleverMenuItem difficultyItem = component.MenuItems.FirstOrDefault(m => m.name == "difficulty");
            CleverMenuItem feedbackItem = component.MenuItems.FirstOrDefault(m => m.name == "MOD FEEDBACK");
            CleverMenuItem exitItem = component.MenuItems.FirstOrDefault(m => m.name == "exit");

            if (difficultyItem != null && exitItem != null && continueItem != null && optionsItem != null && feedbackItem != null)
            {
                // Calculate spacing between menu items
                float spacing = Mathf.Abs(continueItem.transform.localPosition.y - optionsItem.transform.localPosition.y);

                // Position MOD FEEDBACK one spacing unit below difficulty (between difficulty and exit)
                Vector3 feedbackPos = difficultyItem.transform.localPosition;
                feedbackPos.y -= spacing;
                feedbackItem.transform.localPosition = feedbackPos;

                // Shift exit down by the same amount
                Vector3 exitPos = difficultyItem.transform.localPosition;
                exitPos.y -= spacing * 2; // Two units down (difficulty -> feedback -> exit)
                exitItem.transform.localPosition = exitPos;
            }
            else if (exitItem != null && feedbackItem != null)
            {
                // Fallback: just place feedback above exit
                feedbackItem.transform.localPosition = exitItem.transform.localPosition + new Vector3(0, 0.5f, 0);
            }
        }

        private System.Collections.IEnumerator OpenFeedbackWindowNextFrame(CleverMenuItemSelectionManager component)
        {
            yield return null; // Wait one frame to prevent click passthrough

            GameObject feedbackObj = new GameObject("FeedbackWindow");
            FeedbackWindow window = feedbackObj.AddComponent<FeedbackWindow>();
            window.Initialize((string feedback) =>
            {
                feedbackSender.SendFeedback(feedback);
                UnityEngine.Object.Destroy(window.gameObject);
                component.IsSuspended = false;
            }, () =>
            {
                component.IsSuspended = false;
            });
        }
    }
}
