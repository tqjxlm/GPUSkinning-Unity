using UnityEngine;
using UnityEngine.UI;

public class HardwareAdapter
{
    public static bool MortonSortEnabled { get; private set; } = true;

    static readonly string prefKey = "MortonSortEnabled";

    public static int TargetFrameRate { get; private set; } = 25;

    [RuntimeInitializeOnLoadMethod]
    static void OnRuntimeMethodLoad()
    {
        // Get the settings toggle
        GameObject MortonSortToggleWidget = GameObject.Find("MortonSortToggle");
        Toggle MortonSortToggle = MortonSortToggleWidget.GetComponent<Toggle>();

        if (!PlayerPrefs.HasKey(prefKey))
        {
            // Detect the default settings at first start
            bool isMaliGPU = SystemInfo.graphicsDeviceName.ToLower().Contains("mali");
            MortonSortEnabled = !isMaliGPU;

            PlayerPrefs.SetInt(prefKey, MortonSortEnabled ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("Created user pref: " + prefKey);

            MortonSortToggle.isOn = MortonSortEnabled;
        }
        else
        {
            // Use user preference after first start
            MortonSortEnabled = PlayerPrefs.GetInt(prefKey) == 1;
            MortonSortToggle.isOn = MortonSortEnabled;
        }

        Debug.Log((MortonSortEnabled ? "Enabled" : "Disabled") + " Morton Sort on Device " + SystemInfo.graphicsDeviceName);

        // Save user preference when it changes
        MortonSortToggle.onValueChanged.AddListener(delegate {
            PlayerPrefs.SetInt(prefKey, MortonSortToggle.isOn ? 1 : 0);
            PlayerPrefs.Save();
        });
    }
}
