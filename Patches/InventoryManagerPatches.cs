using HarmonyLib;
using OriBFArchipelago.ArchipelagoUI;
using OriBFArchipelago.Core;
using OriBFArchipelago.MapTracker.Core;
using UnityEngine;

namespace OriBFArchipelago.Patches
{
    internal class InventoryManagerPatches
    {
        [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.Awake))]
        public static class InventoryManager_Awake_Patch
        {
            [HarmonyPostfix]
            static void Awake_Postfix(InventoryManager __instance)
            {
                // Add our GUI drawer component
                __instance.gameObject.AddComponent<InventoryButtonHintDrawer>();
            }
        }

        public class InventoryButtonHintDrawer : MonoBehaviour
        {
            private InventoryManager inventoryManager;
            private Transform legendTransform;
            private GameObject lbButtonHint;
            private GameObject rbButtonHint;
            private GameObject apStatsHint;
            private bool hintsCreated = false;
            private bool wasVisible = false;
            private bool wasKeyboardUsedLast = false;
            private StatsPage lastPage = StatsPage.Vanilla;


            void Awake()
            {
                inventoryManager = GetComponent<InventoryManager>();
            }

            void Start()
            {
                CreateButtonHints();
            }

            void CreateButtonHints()
            {
                ModLogger.Debug("Creating custom button hints");

                legendTransform = inventoryManager.transform.Find("legend");
                if (legendTransform == null)
                {
                    ModLogger.Debug("Legend object not found!");
                    return;
                }

                Transform backButton = legendTransform.Find("back");
                Transform selectButton = legendTransform.Find("select");
                Transform navigateButton = legendTransform.Find("navigate");

                if (backButton == null)
                {
                    ModLogger.Debug("Back button template not found!");
                    return;
                }

                // Calculate spacing between existing buttons
                float spacing = 0f;
                if (navigateButton != null && selectButton != null)
                {
                    spacing = selectButton.localPosition.x - navigateButton.localPosition.x;
                }

                // Create LB button hint
                lbButtonHint = UnityEngine.Object.Instantiate(backButton.gameObject);
                lbButtonHint.transform.SetParent(legendTransform);
                lbButtonHint.name = "lb_custom";

                Vector3 lbPos = navigateButton != null ? navigateButton.localPosition : backButton.localPosition;
                lbPos.x -= (spacing > 0 ? spacing * 3 : 4f);
                lbButtonHint.transform.localPosition = lbPos;
                lbButtonHint.transform.localRotation = backButton.localRotation;
                lbButtonHint.transform.localScale = backButton.localScale;

                // Create RB button hint
                rbButtonHint = UnityEngine.Object.Instantiate(backButton.gameObject);
                rbButtonHint.transform.SetParent(legendTransform);
                rbButtonHint.name = "rb_custom";

                Vector3 rbPos = backButton.localPosition;
                rbPos.x += (spacing > 0 ? spacing : 2f);
                rbButtonHint.transform.localPosition = rbPos;
                rbButtonHint.transform.localRotation = backButton.localRotation;
                rbButtonHint.transform.localScale = backButton.localScale;

                // Create the archipelago statistics hint. It sits left of the teleport hint,
                // but its position is measured rather than fixed - see PositionApStatsHint.
                apStatsHint = UnityEngine.Object.Instantiate(backButton.gameObject);
                apStatsHint.transform.SetParent(legendTransform);
                apStatsHint.name = "ap_stats_custom";
                apStatsHint.transform.localPosition = lbPos;
                apStatsHint.transform.localRotation = backButton.localRotation;
                apStatsHint.transform.localScale = backButton.localScale;

                hintsCreated = true;
                ModLogger.Debug("Button hints created successfully");
            }

            /**
             * Places the statistics hint clear of the teleport hint.
             *
             * The other two hints use fixed offsets, which is fine for two entries of known
             * length but breaks down as soon as a third is added - the label lengths differ
             * per page and per input scheme. Measuring the rendered width the way
             * MapPanelController.LayoutLegend does keeps them from overlapping.
             */
            private void PositionApStatsHint()
            {
                if (apStatsHint == null || lbButtonHint == null || legendTransform == null)
                    return;

                float legendLossyX = legendTransform.lossyScale.x;
                float width = MeasureLocalWidth(apStatsHint.transform, legendLossyX, 4f);
                float gap = MeasureLocalWidth(lbButtonHint.transform, legendLossyX, 4f) * 0.15f;

                Vector3 position = lbButtonHint.transform.localPosition;
                position.x -= width + gap;
                apStatsHint.transform.localPosition = position;
            }

            private static float MeasureLocalWidth(Transform entry, float legendLossyX, float fallback)
            {
                if (entry == null || legendLossyX <= 0f)
                    return fallback;

                Renderer[] renderers = entry.GetComponentsInChildren<Renderer>(true);
                bool any = false;
                Bounds bounds = new Bounds();
                foreach (Renderer r in renderers)
                {
                    if (!any)
                    {
                        bounds = r.bounds;
                        any = true;
                    }
                    else
                    {
                        bounds.Encapsulate(r.bounds);
                    }
                }

                if (!any || bounds.size.x <= 0f)
                    return fallback;

                return bounds.size.x / legendLossyX;
            }

            private void UpdateApStatsButtonText()
            {
                if (apStatsHint == null) return;

                string label;
                switch (ApStatsPages.Page)
                {
                    case StatsPage.ApGlobal: label = "Area statistics"; break;
                    case StatsPage.ApAreas: label = "Game statistics"; break;
                    default: label = "Archipelago statistics"; break;
                }

                SetButtonHintText(apStatsHint, $"{GetLegendButtonIcon()}  {label}");
                PositionApStatsHint();
            }

            // Taken from ButtonIconUtility's own icon table, whose fields are private consts.
            // Worth copying exactly rather than guessing: the letters are not sequential by
            // button, and <icon>h</> is X rather than Y.
            private const string IconButtonY = "<icon>i</>";
            private const string IconKeyboardL = "<icon>P</>";

            /**
             * Core.Input.Legend is the Y button on a controller and L on the keyboard by default
             */
            private string GetLegendButtonIcon()
            {
                return PlayerInput.Instance.WasKeyboardUsedLast ? IconKeyboardL : IconButtonY;
            }

            private void UpdateLBButtonText()
            {
                if (lbButtonHint == null) return;

                string buttonIcon = GetLBButtonIcon();
                SetButtonHintText(lbButtonHint, $"{buttonIcon}  Teleport to start");
            }

            private void UpdateRBButtonText()
            {
                if (rbButtonHint == null) return;

                string buttonIcon = GetRBButtonIcon();
                SetButtonHintText(rbButtonHint, $"{buttonIcon}  Teleport menu");
                rbButtonHint.SetActive(true);
            }

            private string GetLBButtonIcon()
            {
                if (PlayerInput.Instance.WasKeyboardUsedLast)
                {
                    return "F3";
                }
                else
                {
                    return "<icon>R</>"; // Left Shoulder
                }
            }

            private string GetRBButtonIcon()
            {
                if (PlayerInput.Instance.WasKeyboardUsedLast)
                {
                    return "F4";
                }
                else
                {
                    return "<icon>S</>"; // Right Shoulder
                }
            }

            private void SetButtonHintText(GameObject buttonHint, string newText)
            {
                Component messageBoxComponent = buttonHint.GetComponent("MessageBox");
                if (messageBoxComponent == null)
                {
                    return;
                }

                var messageBoxType = messageBoxComponent.GetType();

                // Clear MessageProvider so OverrideText takes precedence
                var messageProviderField = messageBoxType.GetField("MessageProvider",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (messageProviderField != null)
                {
                    messageProviderField.SetValue(messageBoxComponent, null);
                }

                var overrideTextField = messageBoxType.GetField("OverrideText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (overrideTextField != null)
                {
                    overrideTextField.SetValue(messageBoxComponent, newText);

                    var refreshMethod = messageBoxType.GetMethod("RefreshText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                    if (refreshMethod != null)
                    {
                        refreshMethod.Invoke(messageBoxComponent, null);
                    }
                }
            }

            void OnGUI()
            {
                if (!hintsCreated) return;

                bool isVisible = inventoryManager.NavigationManager.IsVisible;
                bool currentKeyboardState = PlayerInput.Instance.WasKeyboardUsedLast;
                StatsPage currentPage = ApStatsPages.Page;

                // Update when menu opens OR when input method changes OR when the statistics
                // page is cycled, since the hint names the page it will switch to
                if ((isVisible && !wasVisible)
                    || (isVisible && currentKeyboardState != wasKeyboardUsedLast)
                    || (isVisible && currentPage != lastPage))
                {
                    UpdateLBButtonText();
                    UpdateRBButtonText();
                    UpdateApStatsButtonText();
                    wasKeyboardUsedLast = currentKeyboardState;
                    lastPage = currentPage;
                }

                wasVisible = isVisible;

                if (lbButtonHint != null)
                {
                    lbButtonHint.SetActive(isVisible);
                }

                if (rbButtonHint != null)
                {
                    rbButtonHint.SetActive(isVisible);
                }

                if (apStatsHint != null)
                {
                    // Only offered in an archipelago run; there are no stats otherwise
                    apStatsHint.SetActive(isVisible && RunStatsTracker.Current != null);
                }
            }
        }
    }
}