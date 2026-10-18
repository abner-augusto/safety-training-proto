using System.Collections.Generic;
using SafetyProto.Core.Interfaces;
using UnityEngine;
using UnityEngine.Events;

namespace SafetyProto.UI
{
    [System.Serializable]
    public class PopupData
    {
        public PopupType type;
        public string title;
        [TextArea(2, 5)]
        public string body;

        public Sprite customIcon;

        public bool hideIcon;

        public Sprite[] illustrationFrames;

        public string actionButtonLabel;
        public UnityEvent onActionPressed;

        public bool showSkipButton;
        public UnityEvent onSkipPressed;

        public string skipButtonLabel;

        public bool showInputField;

        public bool requireInputForAction;

        public bool showNumericKeypad;
        public int keypadMaxDigits = 3;

        public float autoCloseSeconds = 0f;

        [System.NonSerialized] public IReadOnlyList<IReportOption> choiceOptions;
        public UnityAction<IReportOption> onChoiceSelected;
    }
}
