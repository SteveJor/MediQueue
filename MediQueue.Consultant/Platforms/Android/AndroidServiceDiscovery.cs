using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Android.Content;
using Android.Net.Nsd;
using Android.Net.Wifi;

namespace MediQueue.Consultant.Services.Network;

/// <summary>
/// Découverte DNS-SD (mDNS) avec NsdManager, l'API native d'Android. Isolée derrière IServiceDiscovery : aucune règle métier ici.
///
/// - Verrou multicast tenu uniquement pendant la recherche (nécessaire sur les anciennes versions d'Android, inoffensif ailleurs).
/// - NsdManager ne sait résoudre qu'UN service à la fois sur plusieurs versions : les résolutions sont mises en file.
/// - On ne retient que les adresses IPv4 (les IPv6 link-local ne sont pas joignables sans identifiant d'interface).
/// - Arrêter l'énumération (ou annuler) arrête la recherche : jamais de recherche permanente.
///
/// À valider sur appareil réel. Android 17 : pour une application visant l'API 37 ou plus, la permission d'exécution
/// ACCESS_LOCAL_NETWORK sera exigée pour la découverte sur réseau local (voir docs/RESEAU-DECOUVERTE-TLS.md).
/// </summary>
public sealed class AndroidServiceDiscovery : IServiceDiscovery
{
    public async IAsyncEnumerable<DiscoveredService> BrowseAsync(
        string serviceType, [EnumeratorCancellation] CancellationToken ct)
    {
        var context = Android.App.Application.Context;
        var nsd = context.GetSystemService(Context.NsdService) as NsdManager
                  ?? throw new ServerLocationException(ServerLocationError.DiscoveryUnavailable,
                      "La recherche automatique du serveur n'est pas disponible sur cet appareil.");
        var wifi = context.GetSystemService(Context.WifiService) as WifiManager;

        var multicastLock = wifi?.CreateMulticastLock("mediqueue-discovery");
        multicastLock?.SetReferenceCounted(false);
        multicastLock?.Acquire();

        var channel = Channel.CreateUnbounded<DiscoveredService>();
        var listener = new DiscoveryListener(nsd, channel.Writer);
        try
        {
            nsd.DiscoverServices(serviceType, NsdProtocol.DnsSd, listener);
            await foreach (var service in channel.Reader.ReadAllAsync(ct))
                yield return service;
        }
        finally
        {
            listener.Stop();
            channel.Writer.TryComplete();
            if (multicastLock is { IsHeld: true }) multicastLock.Release();
        }
    }

    private sealed class DiscoveryListener : Java.Lang.Object, NsdManager.IDiscoveryListener
    {
        private readonly NsdManager _nsd;
        private readonly ChannelWriter<DiscoveredService> _writer;
        private readonly Queue<NsdServiceInfo> _pending = new();
        private readonly object _lock = new();
        private bool _resolving;
        private bool _stopped;

        public DiscoveryListener(NsdManager nsd, ChannelWriter<DiscoveredService> writer)
        {
            _nsd = nsd;
            _writer = writer;
        }

        public void OnDiscoveryStarted(string? serviceType) { }
        public void OnDiscoveryStopped(string? serviceType) { }
        public void OnServiceLost(NsdServiceInfo? serviceInfo) { }
        public void OnStopDiscoveryFailed(string? serviceType, NsdFailure errorCode) { }

        public void OnStartDiscoveryFailed(string? serviceType, NsdFailure errorCode)
            => _writer.TryComplete(new ServerLocationException(ServerLocationError.DiscoveryUnavailable,
                $"La recherche du serveur a échoué ({errorCode})."));

        public void OnServiceFound(NsdServiceInfo? serviceInfo)
        {
            if (serviceInfo is null) return;
            lock (_lock) _pending.Enqueue(serviceInfo);
            ResolveNext();
        }

        public void Stop()
        {
            lock (_lock)
            {
                _stopped = true;
                _pending.Clear();
            }
            try { _nsd.StopServiceDiscovery(this); }
            catch (Java.Lang.IllegalArgumentException) { /* déjà arrêtée */ }
        }

        private void ResolveNext()
        {
            NsdServiceInfo? next;
            lock (_lock)
            {
                if (_resolving || _stopped || !_pending.TryDequeue(out next)) return;
                _resolving = true;
            }
#pragma warning disable CA1422, CS0618   // ResolveService est « dépréciée » depuis l'API 34 mais reste la seule voie commune à toutes les versions
            try { _nsd.ResolveService(next, new ResolveListener(this)); }
            catch (Exception) { Done(); }
#pragma warning restore CA1422, CS0618
        }

        private void Done()
        {
            lock (_lock) _resolving = false;
            ResolveNext();
        }

        private void Resolved(NsdServiceInfo? info)
        {
            try
            {
                var host = info?.Host?.HostAddress;
                if (info is not null && !string.IsNullOrEmpty(host) && !host.Contains(':'))   // IPv4 seulement
                {
                    var txt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (info.Attributes is { } attributes)
                        foreach (var pair in attributes)
                            txt[pair.Key] = pair.Value is null ? string.Empty : Encoding.UTF8.GetString(pair.Value);
                    _writer.TryWrite(new DiscoveredService(info.ServiceName ?? string.Empty, host, info.Port, txt));
                }
            }
            finally
            {
                Done();
            }
        }

        private sealed class ResolveListener : Java.Lang.Object, NsdManager.IResolveListener
        {
            private readonly DiscoveryListener _owner;
            public ResolveListener(DiscoveryListener owner) => _owner = owner;
            public void OnResolveFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) => _owner.Done();
            public void OnServiceResolved(NsdServiceInfo? serviceInfo) => _owner.Resolved(serviceInfo);
        }
    }
}
