using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Thesis.Orchestration.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public class ConsistentButtonPressFeedback : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerExitHandler,
        ISubmitHandler
    {
        private Button button;
        private Graphic targetGraphic;
        private Color normalColor;
        private Color disabledColor;
        private Color pressedColor;
        private float keyboardPressDuration;
        private bool pointerPressed;
        private Coroutine keyboardPressCoroutine;

        public void Configure(Color sharedPressedColor, float submitDuration)
        {
            button ??= GetComponent<Button>();
            targetGraphic = button.targetGraphic;
            if (targetGraphic == null)
                return;

            normalColor = targetGraphic.color;
            Color disabledMultiplier = button.colors.disabledColor;
            disabledColor = new Color(
                normalColor.r * disabledMultiplier.r,
                normalColor.g * disabledMultiplier.g,
                normalColor.b * disabledMultiplier.b,
                normalColor.a * disabledMultiplier.a);
            pressedColor = sharedPressedColor;
            keyboardPressDuration = Mathf.Max(0.05f, submitDuration);

            // Avoid Unity's persistent Selected tint; this component owns visual states.
            button.transition = Selectable.Transition.None;
            ApplyRestingColor();
        }

        private void LateUpdate()
        {
            if (targetGraphic == null || button == null)
                return;

            if (!button.interactable)
            {
                pointerPressed = false;
                targetGraphic.color = disabledColor;
            }
            else if (pointerPressed)
            {
                targetGraphic.color = pressedColor;
            }
            else if (keyboardPressCoroutine == null)
            {
                targetGraphic.color = normalColor;
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !CanPress())
                return;

            pointerPressed = true;
            targetGraphic.color = pressedColor;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            pointerPressed = false;
            ApplyRestingColor();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerPressed = false;
            ApplyRestingColor();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!CanPress())
                return;

            if (keyboardPressCoroutine != null)
                StopCoroutine(keyboardPressCoroutine);

            keyboardPressCoroutine = StartCoroutine(ShowKeyboardPress());
        }

        private IEnumerator ShowKeyboardPress()
        {
            targetGraphic.color = pressedColor;
            yield return new WaitForSecondsRealtime(keyboardPressDuration);
            keyboardPressCoroutine = null;
            ApplyRestingColor();
        }

        private bool CanPress()
        {
            return button != null && button.IsActive() && button.IsInteractable() && targetGraphic != null;
        }

        private void ApplyRestingColor()
        {
            if (targetGraphic == null || button == null)
                return;

            targetGraphic.color = button.interactable ? normalColor : disabledColor;
        }

        private void OnDisable()
        {
            pointerPressed = false;
            if (keyboardPressCoroutine != null)
            {
                StopCoroutine(keyboardPressCoroutine);
                keyboardPressCoroutine = null;
            }

            ApplyRestingColor();
        }
    }
}
