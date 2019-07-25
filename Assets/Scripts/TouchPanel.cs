using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;


public class TouchPanel : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    Vector2 beginPos;

    // Horizontal movement in screen fraction
    public float Horizontal { get; private set; } = 0;

    // Vertical movement in screen fraction
    public float Vertical { get; private set; } = 0;

    // The maximum screen movement fraction per frame
    public float Radius = 0.2f;

    bool dragging = false;

    Vector2 screenSize;

    // Start is called before the first frame update
    void Start()
    {
        screenSize = new Vector2(Screen.width, Screen.height);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public virtual void OnPointerDown(PointerEventData eventData)
    {
        if (!dragging)
        {
            beginPos = eventData.position;
            dragging = true;
        }
    }

    public virtual void OnDrag(PointerEventData eventData)
    {
        Vector2 endPos = eventData.position;
        Vector2 direction = (endPos - beginPos) / screenSize;
        if (direction.magnitude > Radius)
        {
            direction = direction.normalized * Radius;
        }

        Horizontal = direction.x * 1000;
        Vertical = direction.y * 1000;

        beginPos = endPos;
    }

    public virtual void OnPointerUp(PointerEventData eventData)
    {
        Horizontal = 0;
        Vertical = 0;
        dragging = false;
    }
}
