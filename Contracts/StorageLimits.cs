using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MagitekMonsters.Contracts;

public static class StorageLimits
{
    public const int MaximumBatch = 100;
    public const int BoxCount = 100;
    public const int BoxCapacity = 30;
    public const int TotalCapacity = BoxCount * BoxCapacity;
}

public static class StoragePositions
{
    public static int Position(int box, int slot) => (box - 1) * StorageLimits.BoxCapacity + slot;
    public static int Box(int position) => (position - 1) / StorageLimits.BoxCapacity + 1;
    public static int Slot(int position) => (position - 1) % StorageLimits.BoxCapacity + 1;

    public static string Fingerprint(IEnumerable<(int Position, Guid Pokemon)> occupants)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> entry = stackalloc byte[20];
        foreach (var (position, pokemon) in occupants.OrderBy(occupant => occupant.Position))
        {
            BinaryPrimitives.WriteInt32LittleEndian(entry, position);
            pokemon.TryWriteBytes(entry[4..]);
            hash.AppendData(entry);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
