using System;

/// <summary>
///     Llaman a loading controller desde el manager para mostrar la pantalla
///     Obtiene el progreso
///     le dice al sceneloader que empiece a trabajar
///     Mientras trabaja envia datos al view
///     Termina se llama al close desde el manager
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
        // Reseteamos la vista a 0 antes de mostrarla
        view.UpdateProgress(0f);
    }
}
