// FF.Infrastructure/Persistence/Mongo/Serialization/TolerantDecimalSerializer.cs
using System.Globalization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace FF.Infrastructure.Persistence.Mongo.Serialization;

/// <summary>
/// Reads <c>decimal</c> from whatever BSON type it happens to be stored as; always
/// writes <see cref="BsonType.Decimal128"/>. FAN-129.
///
/// <para>
/// The driver's default serializer persists <c>decimal</c> as a BSON <b>string</b>.
/// The C# side never noticed — it round-trips back to <c>decimal</c> correctly — but
/// every string sorts above every number in BSON order, and strings compare
/// character by character, so anything the <i>server</i> evaluated was wrong:
/// <c>"9.93"</c> outranked <c>"22.64"</c>. That silently misordered ranked lists and,
/// where a <c>Limit()</c> followed the sort, selected the wrong documents outright.
/// </para>
///
/// <para>
/// Switching to Decimal128 by attribute alone would be a breaking change: existing
/// string values would fail to deserialize the moment the new code shipped, and no
/// ordering of migrate-then-deploy avoids a window where one side is wrong. Reading
/// tolerantly removes that constraint. This can ship on its own; every subsequent
/// write lands as Decimal128, and the backfill of existing string values can run
/// whenever it suits (see FAN-129 for the migration).
/// </para>
///
/// <para>
/// Server-side sorts and range filters on decimal fields stay unreliable until that
/// backfill has run against the environment in question. The in-memory sorts in
/// PlayerProjectionRepository, SimulationResultRepository, VorpRecommendationRepository
/// and EmergenceAlertRepository must stay exactly as they are until then.
/// </para>
/// </summary>
public sealed class TolerantDecimalSerializer : StructSerializerBase<decimal>
{
    private const NumberStyles DecimalStyles =
        NumberStyles.Float | NumberStyles.AllowThousands;

    public override decimal Deserialize(
        BsonDeserializationContext context,
        BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        var bsonType = reader.GetCurrentBsonType();

        switch (bsonType)
        {
            case BsonType.Decimal128:
                return Decimal128.ToDecimal(reader.ReadDecimal128());

            // The legacy representation. Still the majority of stored values until
            // the backfill runs.
            case BsonType.String:
                var raw = reader.ReadString();
                if (decimal.TryParse(raw, DecimalStyles, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
                throw new FormatException(
                    $"Cannot deserialize '{raw}' as decimal — the stored string is not a number.");

            // Not produced by this application, but a hand-edit in Compass or a
            // mongosh backfill can leave a field as a plain double or int.
            case BsonType.Double:
                return (decimal)reader.ReadDouble();

            case BsonType.Int32:
                return reader.ReadInt32();

            case BsonType.Int64:
                return reader.ReadInt64();

            // Deliberately NOT mapped to 0m. A null in a non-nullable decimal is a
            // schema violation, and inventing a zero here would manufacture exactly
            // the kind of plausible-looking fake projection this codebase has spent
            // real time hunting down. Declare the property as decimal? if null is a
            // legitimate value — NullableSerializer handles it before reaching here.
            default:
                throw new FormatException(
                    $"Cannot deserialize BSON type '{bsonType}' as decimal.");
        }
    }

    public override void Serialize(
        BsonSerializationContext context,
        BsonSerializationArgs args,
        decimal value)
        => context.Writer.WriteDecimal128(new Decimal128(value));
}

/// <summary>
/// Installs the BSON serializers this application overrides. Must run before the
/// first Mongo read or write — the driver caches a serializer per type on first
/// use and will not replace it afterwards.
/// </summary>
public static class MongoSerializationConfig
{
    private static bool _registered;
    private static readonly object Gate = new();

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered) return;

            var tolerant = new TolerantDecimalSerializer();

            // TryRegister rather than Register: registration throws if the driver has
            // already cached a serializer for the type, and this can legitimately be
            // reached twice (API host plus a test fixture in the same process).
            BsonSerializer.TryRegisterSerializer<decimal>(tolerant);

            // Registered explicitly rather than relying on the driver to wrap the
            // above, so that decimal? gets the tolerant reader even if something has
            // already resolved a serializer for the nullable form.
            BsonSerializer.TryRegisterSerializer<decimal?>(
                new NullableSerializer<decimal>(tolerant));

            _registered = true;
        }
    }
}
