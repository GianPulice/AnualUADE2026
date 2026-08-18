using UnityEngine;

// Debug-only harness: forces a Win/Lose result without playing through the game.
// Not part of the shipping build — remove (or wrap in #if UNITY_EDITOR) before release.
// NOTE: resetKey collides with PlayerStateManager's debug 'R' (Hidden state toggle).
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
