using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A GraphicRaycaster that clicks through a CRT tube. Before hit-testing, the pointer is moved from
/// where the player sees it to where that pixel really sits on the canvas, with the same curvature
/// the screen shader applies (<see cref="CanvasCRTPresenter.TryUnwarp"/>). Without it, buttons near
/// the edges would answer a few pixels away from where they are drawn.
///
/// It also keeps the canvas's old place in the raycast order. As a Screen Space - Camera canvas it
/// would otherwise rank below every overlay canvas, the HUD among them, which it never did as an overlay.
///
/// With no presenter, or with the presenter off, it behaves exactly like a GraphicRaycaster.
/// </summary>
[AddComponentMenu("WIRED/UI/CRT Warped Raycaster")]
public class CRTWarpedRaycaster : GraphicRaycaster
{
    private CanvasCRTPresenter presenter;
    private Canvas ownCanvas;

    protected override void Awake()
    {
        base.Awake();
        // In the parents too: a dropdown's open list is a canvas of its own under the presented one.
        presenter = GetComponentInParent<CanvasCRTPresenter>(true);
        ownCanvas = GetComponent<Canvas>();
    }

    public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
    {
        if (presenter == null || !presenter.IsPresenting)
        {
            base.Raycast(eventData, resultAppendList);
            return;
        }

        Vector2 seen = eventData.position;
        if (!presenter.TryUnwarp(seen, out Vector2 actual)) return;   // off the tube: nothing to hit

        eventData.position = actual;
        try
        {
            base.Raycast(eventData, resultAppendList);
        }
        finally
        {
            eventData.position = seen;
        }
    }

    public override int sortOrderPriority =>
        ownCanvas != null ? ownCanvas.sortingOrder : base.sortOrderPriority;

    public override int renderOrderPriority =>
        ownCanvas != null ? ownCanvas.rootCanvas.renderOrder : base.renderOrderPriority;
}
