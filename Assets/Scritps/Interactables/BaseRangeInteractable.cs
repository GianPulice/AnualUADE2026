using UnityEngine;

public abstract class BaseRangeInteractable : MonoBehaviour, IInteractable
{
    private PlayerStateManager playerStateManager;

    private GameObject imageFar;
    private GameObject imageClose;

    private SphereCollider farTrigger;
    private SphereCollider closeTrigger;

    protected bool playerInFarRange;
    protected bool playerInCloseRange;


    protected virtual void Awake()
    {
        GetComponents();
        InitializeReferences();
        ConfigureInteractionZone();
    }

    protected virtual void Update()
    {
        RotateToPlayerDirectionUI();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        UpdateZones(other);
    }

    void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        UpdateZones(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        playerInFarRange = false;
        playerInCloseRange = false;

        UpdateUI();
    }


    public abstract string GetInteractText();

    public bool CanInteract()
    {
        return playerInCloseRange && CanInteractInCloseRange();
    }

    protected abstract bool CanInteractInCloseRange();

    public void Interact()
    {
        if (!CanInteract()) return;
        OnInteract();
    }

    protected abstract void OnInteract();

    public abstract bool IsRepeatable();


    private void GetComponents()
    {
        playerStateManager = FindAnyObjectByType<PlayerStateManager>();

        imageFar = transform.Find("WorldSpaceCanvas").Find("ImageFar").gameObject;
        imageClose = transform.Find("WorldSpaceCanvas").Find("ImageClose").gameObject;
        farTrigger = transform.Find("ColliderFar").GetComponent<SphereCollider>();
        closeTrigger = transform.Find("ColliderClose").GetComponent<SphereCollider>();
    }

    private void InitializeReferences()
    {
        imageFar.SetActive(false);
        imageClose.SetActive(false);

        farTrigger.isTrigger = true;
        closeTrigger.isTrigger = true;

        ApplyConfigRadii();
    }

    private void ApplyConfigRadii()
    {
        if (InteractionManager.Instance == null || InteractionManager.Instance.Config == null) return;

        SO_InteractionManager cfg = InteractionManager.Instance.Config;
        farTrigger.radius = cfg.FarRadius;
        closeTrigger.radius = cfg.CloseRadius;
    }

    private void ConfigureInteractionZone()
    {
        if (closeTrigger == null) return;

        InteractionZone zone = closeTrigger.GetComponent<InteractionZone>();

        if (zone == null)
        {
            closeTrigger.gameObject.AddComponent<InteractionZone>();
        }
    }

    private void UpdateUI()
    {
        if (imageFar != null)
            imageFar.SetActive(playerInFarRange && !playerInCloseRange);

        if (imageClose != null)
            imageClose.SetActive(playerInCloseRange);
    }

    private void RotateToPlayerDirectionUI()
    {
        if (!playerInFarRange && !playerInCloseRange) return;
        if (playerStateManager == null) return;

        if (imageFar != null)
        {
            Vector3 targetPosition = playerStateManager.transform.position;
            targetPosition.y = imageFar.transform.position.y;

            imageFar.transform.LookAt(targetPosition);
        }

        if (imageClose != null)
        {
            Vector3 targetPosition = playerStateManager.transform.position;
            targetPosition.y = imageClose.transform.position.y;

            imageClose.transform.LookAt(targetPosition);
        }
    }

    private void UpdateZones(Collider player)
    {
        Vector3 playerPosition = player.transform.position;

        if (farTrigger != null)
        {
            float farRadius = farTrigger.radius * farTrigger.transform.lossyScale.x;
            float farDistance = Vector3.Distance(playerPosition, farTrigger.transform.position);

            playerInFarRange = farDistance <= farRadius;
        }

        if (closeTrigger != null)
        {
            float closeRadius = closeTrigger.radius * closeTrigger.transform.lossyScale.x;
            float closeDistance = Vector3.Distance(playerPosition, closeTrigger.transform.position);

            playerInCloseRange = closeDistance <= closeRadius;
        }

        UpdateUI();
    }
}
