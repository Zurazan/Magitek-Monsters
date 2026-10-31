using System.Threading.Tasks;
using MagitekMonsters.Services;

namespace MagitekMonsters.Windows;

public sealed partial class MainWindow
{
    private readonly SessionResponses sessionResponses = new();
    private long foregroundGeneration;

    private SessionResponses.Response<T> InSession<T>(Task<T> response) => sessionResponses.Observe(response);
    private SessionResponses.Response InSession(Task response) => sessionResponses.Observe(response);
}
