using UnityEditor;
using UnityEngine;
using System.IO;

public static class EditorUtilities // Made static and removed MonoBehaviour
{
    private static string saveFilePath = Path.Combine(Application.persistentDataPath, "skinsData.json");

    [MenuItem("Tools/Clear Saved Data %#d")] // Ctrl+Shift+D on Windows / Cmd+Shift+D on Mac
    public static void ClearSavedData()
    {
        // Delete PlayerPrefs
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("PlayerPrefs cleared.");

        // Delete JSON file if it exists
        if (File.Exists(saveFilePath))
        {
            File.Delete(saveFilePath);
            Debug.Log($"Deleted file: {saveFilePath}");
        }
        else
        {
            Debug.Log($"File not found: {saveFilePath}");
        }

        // Refresh the Asset Database to update Unity Editor
        AssetDatabase.Refresh();
    }
}
