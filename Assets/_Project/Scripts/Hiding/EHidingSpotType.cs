/// <summary>
/// The three kinds of hiding spot the level authors (Hiding System spec v1.0).
///
/// SERIALIZED BY DESIGNERS, SO VALUES ARE ONLY EVER APPENDED — never inserted, never reordered,
/// never removed. Unity stores an enum field as the integer behind it, so inserting a value here
/// silently turns every authored Locker in the project into whatever took its slot. Same rule the
/// Nemesis already pays for with ENemesisState and ENemesisPredicate.
///
/// The risk ranking is the spec's, and it is what the Nemesis side will read in phase 2:
/// under a table is the only one that does not blind the monster, a locker leaks through its
/// slats, a container is sealed.
/// </summary>
public enum EHidingSpotType
{
    /// <summary>Metal locker. Medium risk: blind except for what leaks through the slats.</summary>
    Locker,

    /// <summary>Under a work table. High risk: it narrows the monster's vision, it does not blind it.</summary>
    UnderTable,

    /// <summary>Cargo container. Low risk: no vision at all, and it muffles the player's breathing.</summary>
    Container,
}
