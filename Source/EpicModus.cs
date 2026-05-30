namespace PawnChronicles
{
    /// <summary>
    /// Defines the intensity tier of a PersonalEpicDef.
    /// Set in XML on the epic def. Determines which evaluator handles the arc,
    /// how many stages it runs, and what world consequences it can produce.
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// DESIGN PRINCIPLE
    /// ═══════════════════════════════════════════════════════════════════════
    /// The modus ladder is not just a length scale - it defines HOW the game
    /// engages the player and HOW the world responds.
    ///
    /// Below Kindle: retroactive. The game narrates what already happened.
    ///   No quest tab. No button to click. The pawn lived a moment and the
    ///   engine stamped it - composing the story from live scraper data and
    ///   writing it to the chronicle. The world may have already changed.
    ///
    /// Kindle and above: prospective. The game demands something of you.
    ///   A real quest with a timer, a location, a condition to meet. Ignore
    ///   it and it fails. Fail it and the consequences compound into the
    ///   next arc. The world does not wait.
    ///
    /// All modus types are purely dynamic. No hardcoded scenarios, names,
    /// or outcomes. Every moment is composed at runtime from PawnDataScraper
    /// and WorldDataScraper symbols - the pawn's actual state, the world's
    /// actual state, everything any mod has registered into any DefDatabase.
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// THE LADDER
    /// ═══════════════════════════════════════════════════════════════════════
    ///
    /// Spark - RETROACTIVE. A single moment the game noticed and stamped.
    ///   Fires when a trigger threshold is crossed (first kill, pain spike,
    ///   skill milestone, a death witnessed nearby, a bond formed). The thing
    ///   already happened. Grammar composes a sentence from what the scraper
    ///   found - the pawn's worktype, what was nearby, what they were carrying.
    ///   May drop an item into inventory, apply a memory thought, or write a
    ///   chronicle entry. Never appears in the quest tab. No button to click.
    ///   Fires at most a few times per day. The world leaves a mark on the pawn.
    ///
    /// Ember - RETROACTIVE. 1-3 small flavor moments, running in parallel.
    ///   Daily texture - relationship friction, a health moment, a mood beat,
    ///   a skill practice, a world reaction. Fires on cooldown when the scraper
    ///   finds something worth noting (sick family member, rivalry tension, pain
    ///   threshold crossed). Expires silently if ignored. Completing one gives
    ///   a small mood buff. A pawn can have active Embers AND an active arc
    ///   simultaneously. The world acknowledges the pawn's daily life.
    ///
    /// Kindle - PROSPECTIVE. 3-stage short arc. The first real demand.
    ///   Something ignites, something complicates it, something resolves it.
    ///   Appears in the quest tab with a timer. Requires the player to act -
    ///   tend to something, move someone, keep a condition met. Low stakes:
    ///   mood consequence only, no backstory change. Most pawns cycle through
    ///   several Kindles before qualifying for Flame. Missing it has a cost.
    ///
    /// Flame - PROSPECTIVE. 4-stage arc. The world starts to notice.
    ///   Two complications before the resolution. Stage selection diverges
    ///   meaningfully by pawn profile - a Trauma-dominant pawn and a
    ///   Violence-dominant pawn run the same Flame and experience different
    ///   stage sequences. Minor faction relation shifts. No backstory swap
    ///   but a chronicle note is written. Demands real player decisions.
    ///
    /// Fire - PROSPECTIVE. 5-stage arc. The pawn is changed permanently.
    ///   The full dramatic arc: inciting moment, complication, crisis, turning
    ///   point, resolution. Adulthood backstory is replaced on completion -
    ///   success and failure both leave permanent marks. World events are
    ///   real: map incidents, faction shifts, site spawns. The player must
    ///   engage with the world to resolve it. Chains into Inferno if the
    ///   pawn's updated profile clears the threshold.
    ///
    /// Inferno - PROSPECTIVE. 5-stage arc with a mid-arc branch.
    ///   At stage 2 the pawn's CURRENT dominant tag (not arc-start profile)
    ///   forces a branch - two parallel middle paths that reconverge at the
    ///   climax. World consequences are mandatory, not optional: every Inferno
    ///   produces a site spawn or a raid. Failure guarantees a mental break.
    ///   Must have completed a Fire arc to qualify. The world is permanently
    ///   marked by what the pawn does here.
    ///
    /// Hellfire - PROSPECTIVE. A chain of 2-4 linked epics, escalating.
    ///   Not a single arc - a meta-arc. Each link is a full epic that inherits
    ///   the pawn's current profile and escalates world consequence tier.
    ///   The chain breaks on any failure. Success across all links spawns a
    ///   named world site or faction alliance tied to the pawn's legend.
    ///   Failure spawns a persistent named threat. A full Hellfire chain
    ///   produces 2-4 backstory swaps, multiple world events, and a permanent
    ///   chronicle entry visible in the pawn's bio tab. Colony must have
    ///   survived at least 2 years. The world will remember.
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// WORLD SCRAPING
    /// ═══════════════════════════════════════════════════════════════════════
    /// All modus types draw from two scrapers running in parallel:
    ///   PawnDataScraper  - every data surface on the pawn (hediffs, traits,
    ///                      skills, records, relations, abilities, genes, etc.)
    ///   WorldDataScraper - every data surface on the world (all factions and
    ///                      their goodwill, all world sites and their types,
    ///                      caravans, active incidents, map things near the
    ///                      pawn, orbital traders, colony wealth and mood,
    ///                      world features, and every WorldObjectDef registered
    ///                      by any mod - from flowers to asteroids to SOS beacons)
    ///
    /// Grammar composes from whatever both scrapers find. No hardcoding.
    /// The pawn IS the story. The world IS the stage.
    ///
    /// ═══════════════════════════════════════════════════════════════════════
    /// QUICK REFERENCE
    /// ═══════════════════════════════════════════════════════════════════════
    ///   Spark    -- retro  ─ no quest tab ─ 1 moment  ─ world notices pawn
    ///   Ember    -- retro  ─ no quest tab ─ 1-3 beats ─ pawn notices world
    ///   Kindle   -- pros   ─ quest tab   ─ 3 stages  ─ mood consequence
    ///   Flame    -- pros   ─ quest tab   ─ 4 stages  ─ faction touch
    ///   Fire     -- pros   ─ quest tab   ─ 5 stages  ─ backstory swap
    ///   Inferno  -- pros   ─ quest tab   ─ 5 stages  ─ branching + world event
    ///   Hellfire -- pros   ─ quest tab   ─ 2-4 epics ─ permanent world mark
    /// </summary>
    public enum EpicModus
    {
        Spark,
        Ember,
        Kindle,
        Flame,
        Fire,
        Inferno,
        Hellfire
    }
}