namespace Darklands.Content.Formats.Audio;

/// <summary>
/// Raw sampled-audio payload used by OPENDARK.DGT.
/// </summary>
public sealed class Dgt
{
    public Dgt(string name, byte[] data)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("DGT resource name is required.", nameof(name));
        }

        Name = name;
        Data = data ?? throw new ArgumentNullException(nameof(data));
    }

    public string Name { get; }

    public byte[] Data { get; }

    public int Length => Data.Length;

    public byte[] ReadWindow(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > Data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "DGT window must fit inside the payload.");
        }

        var window = new byte[length];
        Array.Copy(Data, offset, window, 0, length);
        return window;
    }
}

