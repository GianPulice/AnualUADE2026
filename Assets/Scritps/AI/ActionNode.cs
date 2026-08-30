using System;

/// <summary>
/// A leaf: does something and ends the walk.
///
/// The action returns nothing, so a tree that has to produce a VALUE works by having its leaves
/// write that value somewhere the caller reads after the walk. See NemesisDecision, which does
/// exactly that and explains why it is not a limitation worth designing around.
/// </summary>
public class ActionNode : ITreeNode
{
    private readonly Action action;

    public ActionNode(Action action)
    {
        this.action = action;
    }

    public void Execute() => action?.Invoke();
}
