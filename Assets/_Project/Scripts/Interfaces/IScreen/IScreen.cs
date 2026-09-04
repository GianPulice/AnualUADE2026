
using Cysharp.Threading.Tasks;
public interface IScreen
{
    UniTask Open();
    UniTask Close();
    bool IsActive { get; }
}
