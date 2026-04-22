using UnityEngine;

public interface IInteractable
{
    KeyCode InteractKey      { get; }
    string ActionLabel       { get; }
    int Priority             { get; }
    Transform Transform      { get; }
    bool CanInteract         { get; }
    string[] AllowedInStates { get; }
    void OnInteract(Collider source);
}