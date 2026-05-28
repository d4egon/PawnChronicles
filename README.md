# Pawn Chronicles

Pawn Chronicles is a dynamic narrative mod for RimWorld that turns character backgrounds, traits, and live gameplay events into real-time personal stories. The mod reads individual pawn data (skills, traits, relationships, and health conditions) alongside the current world state to generate runtime-composed narrative arcs without relying on pre-scripted templates.

## Core Mechanics

The system evaluates gameplay milestones and processes them through three distinct narrative layers.

* **Sparks:** Minor retroactive logs triggered by specific events like a first kill, a severe injury, or a new bond. These moments create a chronicle entry and a temporary mood effect without requiring player intervention.
* **Embers:** Ongoing daily events representing quiet personal milestones, such as practicing skills or processing grief. These run in parallel and expire naturally if ignored.
* **Arcs:** Formal, time-sensitive story objectives that appear in the quest tab. Arcs demand explicit choices, and failing to complete them results in compounding consequences for the pawn.

### Entangled Arcs and Branching Choices

Stories can bridge multiple colonists. Entangled arcs link two separate pawns through shared situations like intense rivalries, mentorships, or mutual grief, binding them to a single quest line with a shared outcome.

During key moments within an arc, the choices presented to you are determined by the pawn's dominant narrative tag. For example, a pawn leaning heavily toward violence will see completely different resolutions than one driven primarily by curiosity, permanently steering their personal development.

## Arc Tiers

| Tier | Scale | Primary Impact |
| --- | --- | --- |
| **Kindle** | 3 stages | Low stakes, resulting in mood adjustments only. |
| **Flame** | 4 stages | Causes minor faction relationship shifts. |
| **Fire** | 5 stages | Permanently changes the pawn, adding a dedicated Arc Story below their childhood and adulthood traits. |
| **Inferno** | 5 stages | Features a mid-arc branch decided by the pawn's dominant tag at that moment. |
| **Hellfire** | 2 to 4 linked epics | Large-scale world events. Success establishes alliances or named sites, while failure spawns specific named threats. |

## Compatibility

* **Save Compatibility:** Safe to add to existing save files at any point.
* **Dependencies:** Requires the Harmony library to be loaded prior to this mod.
* **Mod Integration:** The data scraper scans RimWorld's core registries automatically. Custom traits, factions, world objects, genes, and backstories added by other mods are dynamically incorporated into the narrative engine.

## Contributing and Modding

Contributions to add variety, fix typos, or extend content are welcome. The structure relies on distinct XML configuration files for content expansion.

### Adding Narrative Content

* **Flavor Text and Word Variety:** Open `PC_NarrativeGrammar.xml`. Add new list entries to existing rule pools like `pc_lex_scene`, `spark_body`, or `ember_title` to expand text variety immediately across the engine.
* **Arc Stages:** Define the stage behavior, tag prerequisites, and outcomes in `PC_QuestStageDefs.xml`. To make it accessible, add its definition name to the stage pool array within your target epic in `PC_PersonalEpicDefs.xml`.
* **New Epics:** Define the narrative framework in `PC_PersonalEpicDefs.xml`. Link it to a tier by adding its definition name to the appropriate epic pool inside `PC_NarrativePremiseDefs.xml`.
* **Custom Backstories and Thoughts:** Unique backstory outcomes belong in `PC_BackstoryDefs.xml` and are assigned as outcomes in the epic files. Custom mood modifiers belong in `PC_ThoughtDefs.xml` and are linked via stage success or failure outcomes.

### Localization and Translations

User interface elements are stored inside the `Languages/Keyed/English/Translation` directory.

The core text generation engine utilizes expansive rulepacks. Translating the narrative framework involves creating patch operations that target the rule pack string arrays, replacing the English strings with localized entries:

```xml
<Operation Class="PatchOperationReplace">
  <xpath>Defs/RulePackDef[defName="PC_NarrativeGrammar"]/rulePack/rulesStrings</xpath>
  <value>
    <rulesStrings>
      <li>pc_lex_scene->translated text here</li>
    </rulesStrings>
  </value>
</Operation>

```

This patching method applies uniformly to both `PC_NarrativeGrammar` and `PC_EntangledGrammar`.
