using System;

/// <summary>
///     The manager calls the loading controller to show the screen
///     It gets the progress
///     It tells the SceneLoader to start working
///     While it works it sends data to the view
///     When it finishes, Close is called from the manager
/// </summary>

public class LoadingController : BaseScreenController<LoadingView,EmptyScreenModel>
{
    private Progress<float> sceneLoadProgress;

    protected void Awake()
    {
       sceneLoadProgress = new Progress<float>(ReportProgress);
    }
    private void ReportProgress(float progressValue)
    {
        view.UpdateProgress(progressValue);
    }

    public IProgress<float> GetProgressReporter()
    {
        return sceneLoadProgress;
    }

    protected override void OnBeforeOpen()
    {
        // Reset the view to 0 before showing it
        view.UpdateProgress(0f);
    }
}
