namespace Thesis.Orchestration.Backends.Netcode.StandaloneShared
{
    public interface IStandaloneNetworkStateAdapterNetcode
    {
        string CaptureNetworkState();
        void ApplyNetworkState(string stateJson);
    }
}
