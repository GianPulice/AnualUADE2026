using UnityEngine;

public class WinLoseTest : MonoBehaviour
{
    public KeyCode winKey   = KeyCode.I;
    public KeyCode loseKey  = KeyCode.K;
    public KeyCode resetKey = KeyCode.R;

    private void Awake()
    {
        GameResultManager.ResetSession();
    }

    private void Update()
    {
        if (Input.GetKeyDown(winKey))
            GameResultManager.ReportWin(15f, 1);

        if (Input.GetKeyDown(loseKey))
            GameResultManager.ReportLoss(10f, 1);

        if (Input.GetKeyDown(resetKey))
            GameResultManager.ResetSession();
    }
}
