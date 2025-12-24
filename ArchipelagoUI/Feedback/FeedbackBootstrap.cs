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

            var menuInjector = feedbackSenderObject.AddComponent<InventoryMenuInjector>();
            menuInjector.Initialize(feedbackSender);

            bootstrap.BootstrapActions = new Dictionary<string, Action<SceneRoot>>
            {
                ["titleScreenSwallowsNest"] = delegate (SceneRoot sceneRoot)
                {
                    CleverMenuItemSelectionManager component = ((Component)((Component)sceneRoot).transform.Find("ui/group/3. fullGameMainMenu")).GetComponent<CleverMenuItemSelectionManager>();
                    component.AddMenuItem("MOD FEEDBACK", component.MenuItems.Count - 1, delegate
                    {
                        component.enabled = false;
                        GameObject feedbackObj = new GameObject("FeedbackWindow");
                        FeedbackWindow window = feedbackObj.AddComponent<FeedbackWindow>();
                        window.Initialize((string feedback) =>
                        {
                            feedbackSender.SendFeedback(feedback);
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

            if (currentMenu != null && currentMenu != lastMenu)
            {
                bool alreadyExists = currentMenu.MenuItems.Any(m => m.name == "MOD FEEDBACK");
                if (!alreadyExists)
                {
                    AddFeedbackMenuItem(currentMenu);
                    lastMenu = currentMenu;
                }
            }

            if (currentMenu != null && menuItemAdded.ContainsKey(currentMenu) && menuItemAdded[currentMenu])
            {
                MaintainMenuPositions(currentMenu);
            }
        }

        private void AddFeedbackMenuItem(CleverMenuItemSelectionManager component)
        {
            try
            {
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

                var bindingFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

                var pressedField = typeof(CleverMenuItem).GetField("Pressed", bindingFlags);
                if (pressedField != null)
                {
                    pressedField.SetValue(menuItem, null);
                }

                var highlightField = typeof(CleverMenuItem).GetField("Highlight", bindingFlags);
                if (highlightField != null)
                {
                    highlightField.SetValue(menuItem, null);
                }

                var unhighlightField = typeof(CleverMenuItem).GetField("Unhighlight", bindingFlags);
                if (unhighlightField != null)
                {
                    unhighlightField.SetValue(menuItem, null);
                }

                var pressedCallbackField = typeof(CleverMenuItem).GetField("<PressedCallback>k__BackingField", bindingFlags);
                if (pressedCallbackField != null)
                {
                    pressedCallbackField.SetValue(menuItem, null);
                }

                var highlightCallbackField = typeof(CleverMenuItem).GetField("<HighlightCallback>k__BackingField", bindingFlags);
                if (highlightCallbackField != null)
                {
                    highlightCallbackField.SetValue(menuItem, null);
                }

                var unhighlightCallbackField = typeof(CleverMenuItem).GetField("<UnhighlightCallback>k__BackingField", bindingFlags);
                if (unhighlightCallbackField != null)
                {
                    unhighlightCallbackField.SetValue(menuItem, null);
                }

                var activatedField = typeof(CleverMenuItem).GetField("Activated", bindingFlags);
                if (activatedField != null)
                {
                    activatedField.SetValue(menuItem, null);
                }

                var visibleField = typeof(CleverMenuItem).GetField("Visible", bindingFlags);
                if (visibleField != null)
                {
                    visibleField.SetValue(menuItem, null);
                }

                InventoryMenuInjector injector = this;

                Action feedbackAction = delegate
                {
                    component.IsSuspended = true;
                    injector.StartCoroutine(OpenFeedbackWindowNextFrame(component));
                };

                menuItem.PressedCallback += feedbackAction;

                MessageBox messageBox = menuItem.gameObject.GetComponentInChildren<MessageBox>();
                if (messageBox != null)
                {
                    messageBox.SetMessage(new MessageDescriptor("MOD FEEDBACK"));
                }

                menuItem.ApplyColors();
                menuItem.gameObject.SetActive(true);
                component.MenuItems.Insert(insertIndex, menuItem);

                if (component.ItemDirection == CleverMenuItemSelectionManager.Direction.NavigationCage)
                {
                    CleverMenuItem exitItem = component.MenuItems.FirstOrDefault(m => m.name == "exit");

                    if (exitItem != null)
                    {
                        var navigationsToExit = component.Navigation.Where(n => n.To == exitItem).ToList();
                        foreach (var nav in navigationsToExit)
                        {
                            nav.To = menuItem;
                        }

                        var navigationsFromExit = component.Navigation.Where(n => n.From == exitItem).ToList();
                        foreach (var nav in navigationsFromExit)
                        {
                            nav.From = menuItem;
                        }

                        component.Navigation.Add(new CleverMenuItemSelectionManager.NavigationData
                        {
                            From = menuItem,
                            To = exitItem,
                            Condition = null
                        });

                        component.Navigation.Add(new CleverMenuItemSelectionManager.NavigationData
                        {
                            From = exitItem,
                            To = menuItem,
                            Condition = null
                        });
                    }
                }

                component.RefreshVisible();
                menuItemAdded[component] = true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed to add menu item: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void MaintainMenuPositions(CleverMenuItemSelectionManager component)
        {
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
                float spacing = Mathf.Abs(continueItem.transform.localPosition.y - optionsItem.transform.localPosition.y);

                Vector3 feedbackPos = difficultyItem.transform.localPosition;
                feedbackPos.y -= spacing;
                feedbackItem.transform.localPosition = feedbackPos;

                Vector3 exitPos = difficultyItem.transform.localPosition;
                exitPos.y -= spacing * 2;
                exitItem.transform.localPosition = exitPos;
            }
            else if (exitItem != null && feedbackItem != null)
            {
                feedbackItem.transform.localPosition = exitItem.transform.localPosition + new Vector3(0, 0.5f, 0);
            }
        }

        private System.Collections.IEnumerator OpenFeedbackWindowNextFrame(CleverMenuItemSelectionManager component)
        {
            yield return null;

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
