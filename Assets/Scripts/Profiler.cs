using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class Profiler : MonoBehaviour
{
    public int benchmarkDuration = 3;

    public bool DisplayOn = true;

    public float FPS { get; private set; } = 60.0f;

    public float AverageFPS { get; private set; } = 60.0f;

    float deltaTime = 0.0f;

    float measuredFPS = 0.0f;

    List<string> lines = new List<string>();

    void Start()
    {
        StartCoroutine("SampleFPS");
    }

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        FPS = 1.0f / deltaTime;
    }

    IEnumerator SampleFPS()
    {
        for (int i = 1; ; i++)
        {
            measuredFPS += FPS;
            if (i % benchmarkDuration == 0)
            {
                AverageFPS = measuredFPS / benchmarkDuration;
                measuredFPS = 0;
            }
            yield return new WaitForSeconds(1.0f);
        }
    }

    public void ResetDisplay()
    {
        if (DisplayOn)
        {
            // Reset for next frame
            lines.Clear();
            lines.Add("");
        }
    }

    public void Log(string line)
    {
        lines.Add(line);
    }

    void OnGUI()
    {
        if (DisplayOn)
        {
            int w = Screen.width, h = Screen.height;
            GUIStyle style = new GUIStyle();
            Rect rect = new Rect(50, 0, w, h * 2 / 100);
            style.alignment = TextAnchor.UpperLeft;
            style.fontSize = h * 2 / 100;
            style.normal.textColor = new Color(0.0f, 0.0f, 0.5f, 1.0f);

            float msec = deltaTime * 1000.0f;

            lines[0] = string.Format("Frame: {0:0.0} ms ({1:0.} fps)", msec, FPS);
            string text = string.Join("\n", lines);
            GUI.Label(rect, text, style);
        }
    }
}