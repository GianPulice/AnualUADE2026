/// <summary>
/// A node of a decision tree. Execution walks from the root down exactly one path and stops.
///
/// The interface is one method on purpose: a node either asks something and hands over to one of
/// two children (<see cref="QuestionNode"/>) or does something and ends the walk
/// (<see cref="ActionNode"/>). Anything more expressive belongs in the predicate a question closes
/// over, not in the tree.
/// </summary>
public interface ITreeNode
{
    void Execute();
}
