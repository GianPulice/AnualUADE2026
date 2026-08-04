/// <summary>
/// Cualquier UI modal que deba bloquear gameplay (inventario, panel de secuencia,
/// lector de documentos, settings, menu de pausa...) implementa esta interfaz y se
/// registra en UIStateManager al abrirse.
///
/// El UIStateManager se encarga de Time.timeScale, cursor y de decidir quien recibe
/// el input de cancelar (ESC).
/// </summary>
public interface IModalUI
{
    /// <summary>Identificador unico de la modal. Sirve para logs y para evitar dobles Push.</summary>
    string ModalId { get; }

    /// <summary>
    /// true  -> esta modal absorbe el ESC y lo usa para cerrar capas internas o cerrarse a si misma.
    /// false -> el ESC pasa de largo y dispara la pausa por encima (ej: panel de secuencia).
    /// </summary>
    bool ConsumesEscape { get; }

    /// <summary>
    /// true  -> mientras esta modal este abierta, el PauseManager NO puede abrirse encima.
    /// false -> permite que la pausa se superponga.
    /// El menu de pausa en si mismo deberia setear esto en true.
    /// </summary>
    bool BlocksPause { get; }

    /// <summary>
    /// true  -> al estar en el stack, UIStateManager pone Time.timeScale = 0.
    /// false -> no toca el timeScale (el juego sigue corriendo mientras la modal esta abierta).
    /// </summary>
    bool PausesGame { get; }

    /// <summary>Solicitud de cierre externa (por ejemplo, desde el UIStateManager al desapilar).</summary>
    void RequestClose();
}
