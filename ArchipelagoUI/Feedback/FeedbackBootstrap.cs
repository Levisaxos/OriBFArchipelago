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

                // Clone continue/exit button as template (use exit since it's a simple text button)
                CleverMenuItem templateItem = component.MenuItems.FirstOrDefault(m => m.name == "exit");
                if (templateItem == null)
                {
                    templateItem = component.MenuItems.FirstOrDefault(m => m.name == "continue");
                }
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
                    pressedCallbackField.SetValue(menuItem, null);
                }
                else
                {
                    var eventBackingField = typeof(CleverMenuItem).GetField("<PressedCallback>k__BackingField",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (eventBackingField != null)
                    {
                        eventBackingField.SetValue(menuItem, null);
                    }
                }

                // Clear the Activated condition so IsActivated returns true (based on activeInHierarchy only)
                var activatedField = typeof(CleverMenuItem).GetField("Activated",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (activatedField != null)
                {
                    activatedField.SetValue(menuItem, null);
                }

                // Also clear the Visible condition field if it exists
                var visibleField = typeof(CleverMenuItem).GetField("Visible",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (visibleField != null)
                {
                    visibleField.SetValue(menuItem, null);
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

                // Clear OnHighlight callback
                var onHighlightField = typeof(CleverMenuItem).GetField("OnHighlightCallback",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (onHighlightField == null)
                {
                    onHighlightField = typeof(CleverMenuItem).GetField("<OnHighlightCallback>k__BackingField",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                if (onHighlightField != null)
                {
                    onHighlightField.SetValue(menuItem, null);
                }

                // Clear OnUnhighlight callback
                var onUnhighlightField = typeof(CleverMenuItem).GetField("OnUnhighlightCallback",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (onUnhighlightField == null)
                {
                    onUnhighlightField = typeof(CleverMenuItem).GetField("<OnUnhighlightCallback>k__BackingField",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                if (onUnhighlightField != null)
                {
                    onUnhighlightField.SetValue(menuItem, null);
                }

                // Clear Action field if it exists
                var actionField = typeof(CleverMenuItem).GetField("Action",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (actionField != null)
                {
                    actionField.SetValue(menuItem, null);
                }

                // Set the text
                MessageBox messageBox = menuItem.gameObject.GetComponentInChildren<MessageBox>();
                if (messageBox != null)
                {
                    messageBox.SetMessage(new MessageDescriptor("MOD FEEDBACK"));
                }

                menuItem.ApplyColors();

                // Ensure the GameObject is active (IsActivated will return true since Activated field is null)
                menuItem.gameObject.SetActive(true);

                // Insert into the menu
                component.MenuItems.Insert(insertIndex, menuItem);

                // Add navigation connections if using NavigationCage mode
                if (component.ItemDirection == CleverMenuItemSelectionManager.Direction.NavigationCage)
                {
                    // Find the exit menu item
                    CleverMenuItem exitItem = component.MenuItems.FirstOrDefault(m => m.name == "exit");

                    if (exitItem != null)
                    {
                        // Find items that navigate TO exit (going down)
                        var navigationsToExit = component.Navigation.Where(n => n.To == exitItem).ToList();

                        foreach (var nav in navigationsToExit)
                        {
                            // Change their target from exit to MOD FEEDBACK
                            nav.To = menuItem;
                        }

                        // Find items that navigate FROM exit (going back up)
                        var navigationsFromExit = component.Navigation.Where(n => n.From == exitItem).ToList();

                        foreach (var nav in navigationsFromExit)
                        {
                            // Change their source from exit to MOD FEEDBACK
                            nav.From = menuItem;
                        }

                        // Add navigation from MOD FEEDBACK to exit (going down)
                        component.Navigation.Add(new CleverMenuItemSelectionManager.NavigationData
                        {
                            From = menuItem,
                            To = exitItem,
                            Condition = null
                        });

                        // Add navigation from exit back to MOD FEEDBACK (going up)
                        component.Navigation.Add(new CleverMenuItemSelectionManager.NavigationData
                        {
                            From = exitItem,
                            To = menuItem,
                            Condition = null
                        });
                    }
                }

                // Refresh the menu visibility state to ensure our item is properly integrated
                component.RefreshVisible();

                // Mark that we've added the item to this menu
                menuItemAdded[component] = true;
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

            if (feedbackItem != null && !feedbackItem.IsVisible)
            {
                feedbackItem.gameObject.SetActive(true);
            }

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
