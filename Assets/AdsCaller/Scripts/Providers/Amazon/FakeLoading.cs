using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class FakeLoading : MonoBehaviour
{
    public int SceneIndex;

    [SerializeField] Image loadingBar;
    [SerializeField] float MINIMUM_LOADING_TIME = 7f;
    [SerializeField] bool sceneLoad;

    private void OnEnable() => StartCoroutine(FillOverTime(MINIMUM_LOADING_TIME));

    IEnumerator FillOverTime(float duration)
    {
        float startFillAmount = 0;
        
        if (loadingBar)
            startFillAmount = loadingBar.fillAmount;

        float elapsedTime = 0f;
        float targetFillAmount = 1f;

        while (elapsedTime < duration)
        {
            if (loadingBar)
                loadingBar.fillAmount = Mathf.Lerp(startFillAmount, targetFillAmount, elapsedTime / duration);
            elapsedTime += Time.deltaTime;

            yield return null;
        }

        if (loadingBar) // Ensure the fill amount is exactly 1 at the end
            loadingBar.fillAmount = targetFillAmount;

        if (sceneLoad)
        {
            AdsCaller.isSceneLoaded = true;
            SceneManager.LoadScene(SceneIndex);
        }
    }
}
