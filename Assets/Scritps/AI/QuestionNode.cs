using System;

/// <summary>
/// Asks one yes/no question and hands over to the matching child.
///
/// The question is a delegate rather than anything the node itself understands, which is what
/// keeps the tree free of any opinion about what it is deciding: it closes over a predicate that
/// already exists somewhere else, and cannot hold a second, subtly different copy of it.
///
/// A null child ends the walk without doing anything. That is a legitimate shape - a branch that
/// means "nothing to do here" - and not an error.
/// </summary>
public class QuestionNode : ITreeNode
{
    private readonly Func<bool> question;
    private readonly ITreeNode trueNode;
    private readonly ITreeNode falseNode;

    public QuestionNode(Func<bool> question, ITreeNode trueNode, ITreeNode falseNode)
    {
        this.question = question;
        this.trueNode = trueNode;
        this.falseNode = falseNode;
    }

    public void Execute()
    {
        if (question == null) return;

        if (question()) trueNode?.Execute();
        else            falseNode?.Execute();
    }
}
