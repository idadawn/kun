using System.Runtime.CompilerServices;
using Kun.Core.Domain;

// Keep the existing Core identity; the contracts package re-exports the same marker.
[assembly: TypeForwardedTo(typeof(IIntegrationEvent))]
