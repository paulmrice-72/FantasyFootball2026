using System.Reflection;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo.Repositories;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace FF.Tests.Persistence;

/// <summary>
/// FAN-175. <c>DynastyValuationRepository.BuildUpdate</c> sets fields one at a time
/// and says nothing about the ones it omits, so a property added to the document and
/// forgotten here is computed, logged as correct, and silently dropped at the
/// repository boundary.
///
/// <para>
/// That has now happened three times. <c>ModelValue</c> (FAN-159) and
/// <c>RawValue</c> (FAN-166) each carry a comment at their update line describing it
/// as "the failure mode this codebase keeps rediscovering"; <c>IsScored</c> was
/// omitted anyway, immediately below both comments, and surfaced as every
/// calibration basis throwing because the flag reached no document in the
/// collection. The cost each time is a build, a full pipeline run and a debugging
/// session, and the defect is invisible at both ends until something downstream
/// fails for an unrelated-looking reason.
/// </para>
///
/// <para>
/// This test renders the real update and compares its key set against the document's
/// own properties, so the fourth omission fails here in milliseconds instead.
/// </para>
/// </summary>
public class DynastyValuationUpdateCoverageTests
{
    /// <summary>
    /// Properties deliberately outside the <c>$set</c> block, each for a stated
    /// reason. Anything not listed here must be set, and adding to this list should
    /// take an argument rather than a moment.
    /// </summary>
    private static readonly HashSet<string> IntentionallyNotSet = new(StringComparer.Ordinal)
    {
        // The upsert filter key. Setting the field you matched on is a no-op at
        // best, and on an insert it is supplied by the filter itself.
        nameof(DynastyValuationDocument.SleeperPlayerId),

        // SetOnInsert, not Set — an existing document keeps the id it was created
        // with, so it appears under a different operator and is checked separately.
        nameof(DynastyValuationDocument.Id),

        // Conditionally set: only when the incoming value is non-empty, so that a
        // sync carrying a blank name cannot erase a good one. Checked separately.
        nameof(DynastyValuationDocument.PlayerName)
    };

    private static BsonDocument RenderUpdate(DynastyValuationDocument document)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<DynastyValuationDocument>();
        return DynastyValuationRepository
            .BuildUpdate(document)
            .Render(serializer, BsonSerializer.SerializerRegistry)
            .AsBsonDocument;
    }

    private static DynastyValuationDocument SampleDocument() => new()
    {
        Id = "id-1",
        SleeperPlayerId = "4046",
        PlayerId = "player-1",
        PlayerName = "Test Player",
        Position = "WR",
        NflTeam = "SF",
        Age = 26,
        YearsExperience = 3,
        Season = 2026,
        IsScored = true
    };

    [Fact]
    public void BuildUpdate_SetsEveryDocumentProperty()
    {
        var rendered = RenderUpdate(SampleDocument());

        rendered.Contains("$set").Should().BeTrue("the update is built from Set calls");
        var setKeys = rendered["$set"].AsBsonDocument.Names.ToHashSet(StringComparer.Ordinal);

        var expected = typeof(DynastyValuationDocument)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => p.Name)
            .Where(name => !IntentionallyNotSet.Contains(name))
            .ToList();

        var missing = expected.Where(name => !setKeys.Contains(name)).ToList();

        missing.Should().BeEmpty(
            "every property the pipeline computes has to survive the repository "
            + "boundary — a field missing here is written nowhere, read as absent "
            + "everywhere, and reported by nothing. Add it to BuildUpdate, or to "
            + "IntentionallyNotSet with a reason.");
    }

    [Fact]
    public void BuildUpdate_StampsIsScored_IncludingWhenFalse()
    {
        // The false case is the one that matters. A player zeroed by this run must
        // have a previous run's true actively overwritten, not merely left unset —
        // an update list that only wrote the flag when it was true would pass a
        // naive test and leave stale flags in the collection forever.
        var rendered = RenderUpdate(SampleDocument());
        rendered["$set"]["IsScored"].AsBoolean.Should().BeTrue();

        var unscored = SampleDocument();
        unscored.IsScored = false;

        var renderedUnscored = RenderUpdate(unscored);
        renderedUnscored["$set"].AsBsonDocument.Contains("IsScored").Should().BeTrue(
            "a false flag has to be written, or a player zeroed by this run keeps "
            + "the true a previous run left on him");
        renderedUnscored["$set"]["IsScored"].AsBoolean.Should().BeFalse();
    }

    [Fact]
    public void BuildUpdate_KeepsIdOnInsertOnly_AndGuardsAnEmptyPlayerName()
    {
        var rendered = RenderUpdate(SampleDocument());

        rendered.Contains("$setOnInsert").Should().BeTrue();
        rendered["$setOnInsert"].AsBsonDocument.Names.Should().Contain("_id");
        rendered["$set"].AsBsonDocument.Names.Should().Contain("PlayerName");

        var blankName = SampleDocument();
        blankName.PlayerName = string.Empty;

        RenderUpdate(blankName)["$set"].AsBsonDocument.Contains("PlayerName")
            .Should().BeFalse("a blank incoming name must not erase a good stored one");
    }
}
