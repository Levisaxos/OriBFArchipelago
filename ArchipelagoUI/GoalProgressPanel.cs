using OriBFArchipelago.Core;
using System.Collections.Generic;
using UnityEngine;
using CoreInput = Core.Input;

namespace OriBFArchipelago.ArchipelagoUI
{
    /// <summary>
    /// World-map panel that lists progress toward each active goal. Toggled with the
    /// left bumper (or F5); shown in the top-right. A more discoverable alternative to
    /// the GoalProgress keybind (Alt+G), which still works.
    /// </summary>
    internal class GoalProgressPanel : MapPanel
    {
        public override string HintLabel => "Goals";
        public override string ControllerIcon => "<icon>R</>"; // Left Shoulder
        public override KeyCode ToggleKey => KeyCode.F5;
        public override CoreInput.InputButtonProcessor ToggleButton => CoreInput.LeftShoulder;
        public override bool AnchorBottomRight => false;
        public override string Title => "Goal Progress";

        public override List<string> GetLines()
        {
            try
            {
                return RandomizerManager.Connection?.GetGoalProgressLines() ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}
