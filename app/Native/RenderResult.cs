namespace ChiakiNg.Native;

/// <summary>What opening a render device did.</summary>
/// <remarks>
/// PP820: a file of its own now. It was declared beside <c>WasapiRender</c>, which PP816 replaced
/// and this line deletes - and the answer a device open gives is not that class's to own, since
/// <see cref="XAudio2Render"/> gives the same three.
/// </remarks>
public enum RenderResult
{
    /// <summary>Open, running, and playing whatever is written to it.</summary>
    Running,

    /// <summary>There is no active render endpoint, which is a machine with no speakers.</summary>
    NoDevice,

    /// <summary>The engine refused. <see cref="XAudio2Render.LastError"/> holds the HRESULT.</summary>
    Refused,
}
