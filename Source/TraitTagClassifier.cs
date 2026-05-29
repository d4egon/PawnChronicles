using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnChronicles
{
    /// <summary>
    /// Infers narrative tag weights from a TraitDef's mechanical effects.
    ///
    /// Works for ALL traits - vanilla and modded - by reading what the trait
    /// actually does (statOffsets, statFactors, skillGains, aptitudes) rather
    /// than matching by defName. A modded "Shooting Mad" trait that buffs
    /// ShootingAccuracyPawn gets classified into the violence category exactly
    /// the same way Careful Shooter would, because the stat is the same.
    ///
    /// Two-layer lookup per stat / skill:
    ///   Layer 1 - exact defName match in the built-in tables (fast, reliable)
    ///   Layer 2 - keyword scan on defName + label (handles unknown modded defs)
    ///
    /// Results are cached per TraitDef so inference only runs once per def,
    /// regardless of how many pawns share that trait.
    /// </summary>
    public static class TraitTagClassifier
    {
        // ── Cache ─────────────────────────────────────────────────────────────
        private static readonly Dictionary<string, Dictionary<string, float>> _cache
            = new Dictionary<string, Dictionary<string, float>>();

        private static readonly Dictionary<string, float> _empty
            = new Dictionary<string, float>();

        /// <summary>
        /// Returns a {tagKey -> weight} mapping for the given trait def.
        /// Positive weight pushes that tag higher; negative suppresses it.
        /// Cached after first call - safe to call every profile build.
        /// </summary>
        public static Dictionary<string, float> Classify(TraitDef def)
        {
            if (def == null) return _empty;
            if (_cache.TryGetValue(def.defName, out var hit)) return hit;
            var result = ComputeWeights(def);
            _cache[def.defName] = result;
            return result;
        }

        // ── Backstory cache ───────────────────────────────────────────────────
        private static readonly Dictionary<string, Dictionary<string, float>> _backstoryCache
            = new Dictionary<string, Dictionary<string, float>>();

        /// <summary>
        /// Returns a {tagKey -> weight} mapping inferred from a BackstoryDef.
        /// Reads skillGains (mechanical), workDisables (what the pawn cannot do),
        /// and title + description keywords (narrative flavour).
        /// Cached per defName after first call.
        /// </summary>
        public static Dictionary<string, float> ClassifyBackstory(BackstoryDef def)
        {
            if (def == null) return _empty;
            if (_backstoryCache.TryGetValue(def.defName, out var hit)) return hit;
            var result = ComputeBackstoryWeights(def);
            _backstoryCache[def.defName] = result;
            return result;
        }

        /// <summary>Flush all caches. Call if defs reload at runtime.</summary>
        public static void ClearCache()
        {
            _cache.Clear();
            _backstoryCache.Clear();
        }

        // ── Stat -> tag table ──────────────────────────────────────────────────
        // (statDefName, tagKey, scalePerAbsoluteOffset)
        // scale converts raw stat delta to narrative tag weight units.
        // E.g. +0.1 ShootingAccuracyPawn  ->  violence += 0.1 * 45 = 4.5 pts
        private static readonly (string stat, string tag, float scale)[] _statTable =
        {
            // Ranged combat
            ("ShootingAccuracyPawn",              "violence",     45f),
            ("RangedCooldownFactor",              "violence",     20f),
            ("AimingDelayFactor",                 "violence",     20f),
            // Melee combat
            ("MeleeHitChance",                    "violence",     45f),
            ("MeleeDPS",                          "violence",     30f),
            ("MeleeCooldownFactor",               "violence",     20f),
            ("MeleeWeaponDamageFactor",           "violence",     25f),
            // Medicine / healing
            ("MedicalTendQuality",                "healer",       45f),
            ("MedicalSurgerySuccessChanceFactor", "healer",       40f),
            ("MedicalTendSpeed",                  "healer",       20f),
            ("MedicalOperationSpeed",             "healer",       20f),
            // Research / intellect
            ("ResearchSpeed",                     "scholar",      35f),
            // Social / leadership
            ("SocialImpact",                      "leadership",   35f),
            ("NegotiationAbility",                "leadership",   30f),
            ("TradePriceImprovement",             "noble",        30f),
            // Crafting / labour
            ("GeneralLaborSpeed",                 "craft",        18f),
            ("WorkSpeedGlobal",                   "craft",        12f),
            // Animals
            ("TameAnimalChance",                  "animalfriend", 45f),
            ("TrainAnimalChance",                 "animalfriend", 40f),
            // Psychic sensitivity -> slight trauma signal
            ("PsychicSensitivity",                "trauma",       15f),
        };

        // ── Skill -> tag table ─────────────────────────────────────────────────
        // (skillDefName, tagKey, weightPerLevel)
        private static readonly (string skill, string tag, float scale)[] _skillTable =
        {
            ("Shooting",     "violence",     6f),
            ("Melee",        "violence",     6f),
            ("Medicine",     "healer",       6f),
            ("Social",       "leadership",   6f),
            ("Intellectual", "scholar",      6f),
            ("Crafting",     "craft",        6f),
            ("Artistic",     "craft",        4f),
            ("Construction", "craft",        3f),
            ("Animals",      "animalfriend", 6f),
            ("Mining",       "survival",     3f),
            ("Plants",       "survival",     3f),
            ("Cooking",      "survival",     3f),
        };

        // ── Keyword fallback for unrecognised modded defs ─────────────────────
        // Matched against (defName + " " + label).ToLowerInvariant()
        private static readonly (string[] words, string tag, float weight)[] _keywords =
        {
            (new[]{"shoot","gun","ranged","ballistic","aim","snipe"},   "violence",     18f),
            (new[]{"melee","brawl","combat","fight","blade","sword"},   "violence",     18f),
            (new[]{"medic","heal","tend","surg","treat","doctor"},      "healer",       18f),
            (new[]{"research","intellect","learn","study","science"},   "scholar",      18f),
            (new[]{"social","speech","diplo","talk","charm","persuad"}, "leadership",   18f),
            (new[]{"trade","negot","market","price","merchant"},        "noble",        14f),
            (new[]{"craft","smith","forge","tailor","construct","weld"},"craft",        14f),
            (new[]{"animal","tame","train","beast","critter","pet"},    "animalfriend", 18f),
            (new[]{"plant","farm","grow","harvest","agri"},             "survival",     10f),
            (new[]{"cook","food","meal","nutr","chef"},                 "survival",     10f),
            (new[]{"mine","drill","quarry","extract"},                  "survival",      8f),
        };

        // ── Backstory title/description keyword -> tag table ───────────────────
        // Matched against (title + " " + description).ToLowerInvariant()
        // Weight is the base contribution; backstory is a strong identity signal
        // so these are intentionally larger than the stat/skill contributions.
        private static readonly (string[] words, string tag, float weight)[] _backstoryKeywords =
        {
            (new[]{"soldier","guard","marine","mercenary","fighter","warrior","veteran","gunner","rifleman"}, "violence", 28f),
            (new[]{"hunter","ranger","scout","trapper","marksman","sniper"},                                  "violence", 22f),
            (new[]{"doctor","medic","nurse","surgeon","healer","physician","paramedic"},                      "healer",   28f),
            (new[]{"researcher","scientist","scholar","academic","professor","analyst","engineer"},            "scholar",  28f),
            (new[]{"artist","craftsman","smith","builder","carpenter","tailor","welder","architect"},         "craft",    24f),
            (new[]{"farmer","grower","cook","chef","herder","rancher","gardener"},                            "survival", 22f),
            (new[]{"priest","monk","preacher","pilgrim","devotee","acolyte","shaman"},                        "devotion", 26f),
            (new[]{"noble","aristocrat","lord","baron","duchess","heir","courtier","socialite"},              "noble",    26f),
            (new[]{"refugee","exile","drifter","wanderer","castaway","outcast","vagrant","nomad"},            "wandering",24f),
            (new[]{"slave","prisoner","captive","bonded","indentured"},                                       "trauma",   22f),
            (new[]{"pirate","criminal","thief","smuggler","outlaw","gang","cartel"},                          "underworld",26f),
            (new[]{"colonist","settler","pioneer","founder","survivor"},                                      "resilience",16f),
            (new[]{"child","orphan","ward","abandoned","feral"},                                              "trauma",   18f),
            (new[]{"leader","commander","officer","chief","captain","director"},                              "leadership",24f),
            (new[]{"animal","tamer","wrangler","breeder","beastmaster","naturalist"},                         "animalfriend",24f),
        };

        // ── WorkTags -> tag signal (for backstory workDisables) ────────────────
        // If a backstory disables a work category, that tells us about the pawn's
        // background (e.g., pacifist backstory disables Violent work).
        // Positive weight = tag is suggested by this disable.
        // We don't penalise the OPPOSITE tag - a pacifist might still be a healer.
        private static readonly (WorkTags tags, string tag, float weight)[] _workDisableSignals =
        {
            (WorkTags.Violent,      "nurture",   15f),   // can't fight -> more caregiving
            (WorkTags.Violent,      "healer",    12f),   // can't fight -> medical path
            (WorkTags.Caring,       "survival",  10f),   // can't care/doctor -> rougher background
            (WorkTags.Crafting,     "survival",   8f),   // can't craft -> lived off the land
            (WorkTags.Intellectual, "violence",  10f),   // not academic -> action-oriented
        };

        // ── Core inference ────────────────────────────────────────────────────

        private static Dictionary<string, float> ComputeBackstoryWeights(BackstoryDef def)
        {
            var w = new Dictionary<string, float>();

            // ── 1. Skill gains (mechanical - same logic as trait skills) ─────
            if (def.skillGains != null)
                foreach (var gain in def.skillGains)
                    ContributeSkill(w, gain.skill, gain.amount * 0.8f); // 80%: backstory gains are background, not mastery

            // ── 2. Work disables (structural identity signals) ────────────────
            foreach (var (tags, tag, weight) in _workDisableSignals)
                if ((def.workDisables & tags) != WorkTags.None)
                    Add(w, tag, weight);

            // ── 3. Title + description keywords (narrative identity) ──────────
            string text = ((def.title ?? "") + " " + (def.description ?? "")).ToLowerInvariant();
            foreach (var (words, tag, weight) in _backstoryKeywords)
                foreach (var word in words)
                    if (text.Contains(word))
                    {
                        Add(w, tag, weight);
                        break; // one keyword hit per category per backstory
                    }

            return w;
        }

        private static Dictionary<string, float> ComputeWeights(TraitDef def)
        {
            var w = new Dictionary<string, float>();

            foreach (var deg in def.degreeDatas ?? Enumerable.Empty<TraitDegreeData>())
            {
                // stat offsets (absolute)
                if (deg.statOffsets != null)
                    foreach (var mod in deg.statOffsets)
                        ContributeStat(w, mod.stat, mod.value);

                // stat factors (1.0 = neutral; distance from 1 = magnitude)
                if (deg.statFactors != null)
                    foreach (var mod in deg.statFactors)
                        ContributeStat(w, mod.stat, mod.value - 1f);

                // explicit skill gains (+/- levels)
                if (deg.skillGains != null)
                    foreach (var gain in deg.skillGains)
                        ContributeSkill(w, gain.skill, gain.amount);

                // 1.6 aptitudes (permanent effective-level offsets)
                if (deg.aptitudes != null)
                    foreach (var apt in deg.aptitudes)
                        ContributeSkill(w, apt.skill, apt.level * 0.5f);

                // recurring mental break tendency -> mild trauma signal
                if (deg.randomMentalState != null)
                    Add(w, "trauma", 12f);
            }

            return w;
        }

        private static void ContributeStat(Dictionary<string, float> w, StatDef stat, float value)
        {
            if (stat == null || value == 0f) return;

            float sign = value > 0f ? 1f : -1f;
            // Cap magnitude so a single outlier stat doesn't dominate the profile.
            float mag = Mathf.Min(Mathf.Abs(value), 2f);

            // Layer 1 - exact defName match
            foreach (var (sn, tag, scale) in _statTable)
            {
                if (stat.defName == sn)
                {
                    Add(w, tag, sign * mag * scale);
                    return;
                }
            }

            // Layer 2 - keyword scan (modded stats)
            string key = (stat.defName + " " + (stat.label ?? "")).ToLowerInvariant();
            foreach (var (words, tag, weight) in _keywords)
                foreach (var word in words)
                    if (key.Contains(word))
                    {
                        Add(w, tag, sign * weight * 0.5f); // half-weight for inferred hits
                        return;
                    }
        }

        private static void ContributeSkill(Dictionary<string, float> w, SkillDef skill, float amount)
        {
            if (skill == null || amount == 0f) return;

            float sign = amount > 0f ? 1f : -1f;
            float mag  = Mathf.Min(Mathf.Abs(amount), 10f); // cap at 10 levels

            // Layer 1 - exact defName match
            foreach (var (sn, tag, scale) in _skillTable)
            {
                if (skill.defName == sn)
                {
                    Add(w, tag, sign * mag * scale);
                    return;
                }
            }

            // Layer 2 - keyword scan (modded skills)
            string key = (skill.defName + " " + (skill.label ?? "")).ToLowerInvariant();
            foreach (var (words, tag, weight) in _keywords)
                foreach (var word in words)
                    if (key.Contains(word))
                    {
                        Add(w, tag, sign * mag * weight * 0.3f);
                        return;
                    }
        }

        private static void Add(Dictionary<string, float> w, string tag, float amount)
        {
            if (!w.TryGetValue(tag, out float cur)) cur = 0f;
            w[tag] = cur + amount;
        }
    }
}
