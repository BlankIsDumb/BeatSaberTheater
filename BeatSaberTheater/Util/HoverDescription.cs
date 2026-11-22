using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BeatSaberTheater.Util;

/// <summary>
/// A dark version of the game's hover hint: a black, see-through popup with white text that appears just above the
/// UI element the pointer is on. One popup is shared by all elements of a view (see <see cref="HoverDescription"/>).
/// </summary>
internal class HoverPopup
{
    private const float Gap = 0.5f;

    private readonly RectTransform _popup;
    private readonly TextMeshProUGUI _text;

    /// <param name="popup">The popup's background panel. Must not be controlled by a layout group.</param>
    /// <param name="text">The text inside it.</param>
    internal HoverPopup(RectTransform popup, TextMeshProUGUI text)
    {
        _popup = popup;
        _text = text;

        // Show() places the popup's bottom centre at the target's top centre
        _popup.anchorMin = _popup.anchorMax = new Vector2(0.5f, 0.5f);
        _popup.pivot = new Vector2(0.5f, 0f);

        // The pointer passing over the popup must not count as leaving the setting below it
        var canvasGroup = _popup.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = _popup.gameObject.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        Hide();
    }

    internal void Show(RectTransform target, string text)
    {
        _text.text = text;
        var rect = target.rect;
        var targetTop = target.TransformPoint(new Vector3(rect.center.x, rect.yMax, 0f));
        var local = _popup.parent.InverseTransformPoint(targetTop);
        _popup.localPosition = new Vector3(local.x, local.y + Gap, 0f);
        _popup.gameObject.SetActive(true);
    }

    internal void Hide()
    {
        _popup.gameObject.SetActive(false);
    }
}

/// <summary>
/// Shows a description in a <see cref="HoverPopup"/> while the pointer is on a setting. Used instead of BSML's
/// hover-hint, which opens the game's white bubble. Pointer enter/exit events bubble up from the setting's slider or
/// toggle to this component on the setting's root object.
/// </summary>
internal class HoverDescription : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    // Like the game's hover hints, wait briefly so sweeping the pointer across settings doesn't flash popups
    private const float ShowDelay = 0.2f;

    private string _description = "";
    private HoverPopup? _popup;
    private bool _showing;

    internal static void Attach(GameObject target, string description, HoverPopup popup)
    {
        var hover = target.GetComponent<HoverDescription>();
        if (hover == null) hover = target.AddComponent<HoverDescription>();
        hover._description = description;
        hover._popup = popup;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        StopAllCoroutines();
        StartCoroutine(ShowAfterDelay());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        StopAllCoroutines();
        HidePopup();
    }

    // A setting hidden while pointed at (its view closed) gets no exit event, so hide the popup here too
    private void OnDisable()
    {
        HidePopup();
    }

    private IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSeconds(ShowDelay);
        if (_popup == null) yield break;
        _popup.Show((RectTransform)transform, _description);
        _showing = true;
    }

    private void HidePopup()
    {
        if (!_showing) return;
        _showing = false;
        _popup?.Hide();
    }
}
