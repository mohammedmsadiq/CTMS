namespace CTMS.Application.Translations.Export;

/// <summary>
/// A rendered translation export: the bytes plus the HTTP metadata an endpoint needs to stream
/// them back (the endpoint adds no logic of its own).
/// </summary>
public sealed record ExportedFile(string FileName, string ContentType, byte[] Bytes);
