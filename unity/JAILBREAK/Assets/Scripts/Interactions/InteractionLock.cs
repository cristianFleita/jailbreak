/// <summary>
/// Ref-counted lock that blocks <see cref="InteractionManager"/> scanning
/// while an interaction animation is in progress.
///
/// Uses Unity-style null checks (<c>!= null</c>) instead of C#'s <c>?.</c>
/// operator because the manager may be <c>Destroy()</c>-ed on remote
/// avatars — Unity's fake-null is invisible to <c>?.</c> and would cause
/// a <c>MissingReferenceException</c>.
/// </summary>
public class InteractionLock
{
    private readonly InteractionManager manager;
    private bool active;

    public InteractionLock(InteractionManager manager)
    {
        this.manager = manager;
    }

    public void Acquire()
    {
        if (active) return;
        active = true;
        // Unity null-check: respects Destroy()-ed objects
        if (manager != null) manager.PushLock();
    }

    public void Release()
    {
        if (!active) return;
        active = false;
        if (manager != null) manager.PopLock();
    }
}