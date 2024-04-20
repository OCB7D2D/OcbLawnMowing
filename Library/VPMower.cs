using System;
using System.Collections.Generic;
using UnityEngine;

public class VPMower : VehiclePart
{

    // ####################################################################
    // ####################################################################
    
    public bool IsOn = false;
    private float LastBlade = 0f;
    private float LastPlant = 0f;
    private float BladeInterval = 1f / 30f;
    private float PlantInterval = 1f / 8f;
    private float FuelUsePerSecond = 0.01f;
    private Vector2i Area = Vector2i.one;
    private Vector2i Reach = new Vector2i(-1, 2);

    // Handlig for mower audio
    Audio.Handle loop;
    float LoopFadeIn = 0f;
    float LoopFaded = 0f;

    // Counter to alternate toggle
    private int ClickCount = -1;

    // Some mods may reduce damage
    public float DamageModifier = 1f;

    // ####################################################################
    // ####################################################################

    private float LastLightState = 0f;

    private Color ColorBrakeLight =
        new Color(.89f, .09f, .03f, 1);

    private Color ColorMowerOn =
        new Color(.09f, .89f, .03f, 1);

    // Remember if brakes have been on
    // Update material as little as possible
    private float LastBrakes = 0f;

    // ####################################################################
    // ####################################################################

    private ParticleSystem ParticlesSys;
    private int ParticlesPerGrass = 10;

    // Offset for particle emission for mowed down grass (center above ground)
    static private Vector3 particleOffset = new Vector3(0.5f, 0.25f, 0.5f);

    // ####################################################################
    // ####################################################################

    private bool DoReseed = false;
    private bool ProtectGrowingPlants = false;
    // private bool IsWildHarvesterInstalled = false;
    // private bool IsCropHarvesterInstalled = false;

    // ####################################################################
    // ####################################################################

    private readonly List<Material> Materials = new List<Material>();

    private readonly HashSet<string> HarvestTags = new HashSet<string>();
    private readonly HashSet<string> HarvestTools = new HashSet<string>();

    // private readonly Dictionary<string, HashSet<string>> Requirements
    //     = new Dictionary<string, HashSet<string>>();

    private HashSet<string> OldShownModPhysics = new HashSet<string>();
    private HashSet<string> NewShownModPhysics = new HashSet<string>();

    private HashSet<string> OldShownModTransforms = new HashSet<string>();
    private HashSet<string> NewShownModTransforms = new HashSet<string>();

    // ####################################################################
    // ####################################################################
    
    public override void InitPrefabConnections()
    {
        base.InitPrefabConnections();
        Transform ps = GetTransform("particle_transform");
        if (ps == null) return; // Play safe
        ParticlesSys = ps.GetComponentInChildren<ParticleSystem>();
        if (ParticlesSys != null) ParticlesSys.Stop();
        else Log.Warning("Particle System not found");
        if (ParticlesSys == null) return;
        var main = ParticlesSys.main;
        main.maxParticles = 500;
    }

    // ####################################################################
    // ####################################################################
    
    public override void SetProperties(DynamicProperties properties)
    {
        base.SetProperties(properties);
        // Area to mow in front and to the side
        if (properties.Contains("area")) Area = StringParsers
                .ParseVector2i(properties.GetString("area"));
        // Up/down reach of mower (default is one down and two up)
        // ToDo: Check why we need two up in order to mow corn in planters
        if (properties.Contains("reach")) Reach = StringParsers
                .ParseVector2i(properties.GetString("reach"));
        // Interval to check perimeter around mower for stuff to mow down
        properties.ParseFloat("blade_interval", ref BladeInterval);
        // Interval to harvest and reseed growing crops
        properties.ParseFloat("plant_interval", ref PlantInterval);
        // Amount of fuel used per seconds (so mowing costs a little)
        properties.ParseFloat("fuel_per_second", ref FuelUsePerSecond);
        // Number of particles to emit per mowed down plant
        properties.ParseInt("particle_per_grass", ref ParticlesPerGrass);
    }

    // ####################################################################
    // ####################################################################
    
    static string[] SplitAndTrim(string str, char delim)
    {
        var splitted = str.Split(new char[] { delim },
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < splitted.Length; i++)
            splitted[i] = splitted[i].Trim();
        return splitted;
    }

    // Check modifications and pre-cache some information for later
    public void UpdateModifications(ItemValue[] modifications)
    {
        Materials.Clear();
        HarvestTags.Clear();
        HarvestTools.Clear();
        // Requirements.Clear();
        NewShownModPhysics.Clear();
        NewShownModTransforms.Clear();

        if (vehicle == null) return;

        // Collect all materials (in a unique fashion)
        Transform transform = vehicle.GetMeshTransform();
        foreach (var renderer in transform?.GetComponentsInChildren<Renderer>(true))
            if (!Materials.Contains(renderer.material)) Materials.Add(renderer.material);
        // Reset brake lights on initialization
        foreach (var material in Materials)
            material.SetVector("_BrakeColor", Color.black);
        // Collect `MowerHarvestTags` from all modifiers
        foreach (ItemValue mod in modifications)
        {
            var item = mod?.ItemClass;
            var props = item?.Properties;
            var values = props?.Values;
            if (values == null) continue; // Play safe
            props.ParseFloat("DamageModifier", ref DamageModifier);
            if (values.TryGetString("MowerHarvestTags", out string tags))
            {
                var names = SplitAndTrim(tags, ',');
                foreach (string tag in names) HarvestTags.Add(tag);
                //if (values.TryGetString("HarvestRequirements", out tags))
                //{
                //    var reqs = SplitAndTrim(tags, ',');
                //    foreach (string tag in names)
                //    {
                //        if (Requirements.TryGetValue(tag, out var requirement))
                //            foreach (var req in reqs) requirement.Add(req);
                //        else Requirements.Add(tag, new HashSet<string>(reqs));
                //    }
                //}
            }
            if (values.TryGetString("MowerHarvestTools", out string tools))
            {
                foreach (string tool in SplitAndTrim(tools, ','))
                    HarvestTools.Add(tool);
            }
            if (values.TryGetString("EnablePhysics", out string physics))
            {
                foreach (string physic in SplitAndTrim(physics, ','))
                    NewShownModPhysics.Add(physic);
            }
            if (values.TryGetString("ShowTransforms", out string shows))
            {
                foreach (string show in SplitAndTrim(shows, ','))
                    NewShownModTransforms.Add(show);
            }
        }
        // Check if there are any changes to any physics
        if (OldShownModPhysics.Count > 0 || NewShownModPhysics.Count > 0)
        {
            var root = vehicle?.entity?.PhysicsTransform;
            if (root == null) return; // Play safe
            // Hide physics of now removed mods
            foreach (string showing in OldShownModPhysics)
            {
                // Hide if not still shown by new modifiers
                if (NewShownModPhysics.Contains(showing)) continue;
                // Hide transform no longer equipped
                if (root.Find(showing) is Transform shown)
                    shown.gameObject.SetActive(false);

            }
            // Show physics of all current mods
            foreach (string showing in NewShownModPhysics)
            {
                // Show physics that are currently equipped
                if (root.Find(showing) is Transform shown)
                    shown.gameObject.SetActive(true);
            }
            // Update the state to remember what we showed
            // We need to reset that when mod is unequipped
            // Swap the objects to avoid memory allocations
            var swap = OldShownModPhysics;
            OldShownModPhysics = NewShownModPhysics;
            NewShownModPhysics = swap;
        }

        // Check if there are any changes to any transforms
        if (OldShownModTransforms.Count > 0 || NewShownModTransforms.Count > 0)
        {
            var mesh = vehicle.GetMeshTransform();
            // Hide transforms of now removed mods
            foreach (string showing in OldShownModTransforms)
            {
                // Hide if not still shown by new modifiers
                if (NewShownModTransforms.Contains(showing)) continue;
                // Hide transform no longer equipped
                if (mesh.Find(showing) is Transform shown)
                    shown.gameObject.SetActive(false);

            }
            // Show transforms of all current mods
            foreach (string showing in NewShownModTransforms)
            {
                // Show transforms that are currently equipped
                if (mesh.Find(showing) is Transform shown)
                    shown.gameObject.SetActive(true);
            }
            // Update the state to remember what we showed
            // We need to reset that when mod is unequipped
            // Swap the objects to avoid memory allocations
            (NewShownModTransforms, OldShownModTransforms) =
                (OldShownModTransforms, NewShownModTransforms);
        }

        // Cache states for faster run-time during update calls
        ProtectGrowingPlants = HarvestTags.Contains("growProtector");
        // IsWildHarvesterInstalled = HarvestTags.Contains("plantCollector");
        // IsCropHarvesterInstalled = HarvestTags.Contains("cropMowing");
        DoReseed = HarvestTags.Contains("tractorReseed");
    }

    // ####################################################################
    // ####################################################################
    
    private void StopMower()
    {
        if (vehicle == null) return;
        if (loop != null && vehicle != null)
        {
            loop.Stop(vehicle.entity.entityId);
            loop = (Audio.Handle)null;
        }
        PlaySound(properties.Values["sound_shut_off"]);
    }

    private void StartMower()
    {
        if (loop != null) return;
        if (vehicle == null) return;
        if (vehicle.GetHealth() <= 0) return;
        if (vehicle.GetFuelLevel() <= 0.0) return;
        PlaySound(properties.Values["sound_start"]);
        string soundLoop = properties.GetString("sound_loop");
        loop = Audio.Manager.Play(vehicle.entity, soundLoop, wantHandle: true);
        if (loop == null) return;
        loop.SetVolume(0);
        LoopFadeIn = 2f;
        LoopFaded = 0f;
    }

    private void EnableMower(bool enable)
    {
        if (vehicle == null) return;
        if (vehicle.GetHealth() <= 0) enable = false;
        if (vehicle.GetFuelLevel() <= 0.0) enable = false;
        if (IsOn == enable) return;
        IsOn = enable;
        if (vehicle?.entity == null) return;
        if (vehicle.entity.isEntityRemote) return;
        if (enable) StartMower();
        else StopMower();
    }

    private void PlaySound(string _sound)
    {
        if (vehicle == null) return;
        if (vehicle.entity == null) return;
        if (vehicle.entity.isEntityRemote) return;
        vehicle.entity.PlayOneShot(_sound);
    }

    // ####################################################################
    // ####################################################################

    public override void HandleEvent(Event evt, VehiclePart part, float arg)
    {
        if (evt != Event.LightsOn) return;
        if (LastLightState != arg)
        {
            LastLightState = arg;
            if (ClickCount == 3)
                ClickCount = 0;
            else ClickCount += 1;
            EnableMower(ClickCount == 1
                || ClickCount == 2);
        }
        ToggleEmission(arg != 0.0);
    }

    // ####################################################################
    // ####################################################################
    
    public override void HandleEvent(Vehicle.Event _event, float _arg)
    {
        // if (IsBroken()) return;
        switch (_event)
        {
            // Stop mower if it is on, start it if it was one
            // Flag is preserved while the vehicle is not driven
            case Vehicle.Event.Stopped: if (IsOn) StopMower(); break;
            case Vehicle.Event.Started: if (IsOn) StartMower(); break;
        }
    }

    // ####################################################################
    // ####################################################################
    
    private void ToggleEmission(bool state)
    {
        if (vehicle == null) return;
        if (IsBroken()) state = false;
        Transform transform = vehicle.GetMeshTransform();
        if (transform == null) return; // Play safe in case of bad data
        foreach (var renderer in transform?.GetComponentsInChildren<Renderer>(true))
            if (!Materials.Contains(renderer.material)) Materials.Add(renderer.material);
        foreach (var material in Materials)
        {
            // Switch between shader variants (mainly serves as an example)
            if (state) material.EnableKeyword("EMISSION_ON");
            else material.DisableKeyword("EMISSION_ON");
            var color = IsOn ? ColorMowerOn : Color.black;
            material.SetColor("_MowerOnColor", color);
        }
    }

    // ####################################################################
    // ####################################################################

    // Helper to query current vehicle brake state
    static private readonly HarmonyFieldProxy<float> WheelBrakes = new
        HarmonyFieldProxy<float>(typeof(EntityVehicle), "wheelBrakes");
    
    // Main function to drive the lawn mower
    public override void Update(float dt)
    {
        if (vehicle == null) return;

        // Check brake state on every update!?
        // ToDo: maybe debounce if too expensive
        // Although the dynamic call should be ok!?
        float HasBrakes = WheelBrakes.Get(vehicle.entity);
        // Update material if state changed
        if (HasBrakes != LastBrakes)
        {
            Color color = ColorBrakeLight * HasBrakes * 0.5f;
            foreach (var material in Materials)
                material.SetVector("_BrakeColor", color);
            LastBrakes = HasBrakes;
        }

        // Disable if it is broken
        if (IsBroken() && IsOn)
            ToggleEmission(IsOn = false);

        // Check if mower is on
        if (IsOn == false) return;

        // Fade in audio loop
        if (loop != null && LoopFaded < LoopFadeIn)
        {
            LoopFaded += dt;
            if (LoopFaded > LoopFadeIn)
                LoopFaded = LoopFadeIn;
            loop.SetVolume(LoopFaded / LoopFadeIn);
        }

        LastBlade += dt; // add wait time
        LastPlant += dt; // add wait time
        if (LastBlade < BladeInterval) return;
        LastBlade %= BladeInterval; // reset next wait

        // Check if we have an engine and if it is running
        if (vehicle.FindPart("engine") is VPEngine engine)
            if (engine.isRunning == false) return;

        // Use up fuel to operate the blades
        if (FuelUsePerSecond > 0) 
            vehicle.FireEvent(Event.FuelRemove, this,
                FuelUsePerSecond * BladeInterval);

        Vector3i blockPos = vehicle.entity.GetBlockPosition();
        List<BlockChangeInfo> _blockChangeInfo = new List<BlockChangeInfo>();

        World world = vehicle.entity.world;
        GameRandom random = world.GetGameRandom();
        bool hasStorage = vehicle.HasStorage();
        bool hasInventoryChanges = false;

        Vector3i offset = Vector3i.zero;
        for (offset.x = -Area.x; offset.x <= Area.x; offset.x++)
        {
            for (offset.z = -Area.y; offset.z <= Area.y; offset.z++)
            {
                for (offset.y = Reach.x; offset.y <= Reach.y; offset.y++)
                {
                    // Get current block at position to check if mow-able
                    BlockValue block = world.GetBlock(blockPos + offset);
                    // Check if block is harvestable by mower
                    if (!ShouldMowDown(block)) continue;
                    // Determine reseeding/replacement block 
                    // Defaults to replace it with "nothing"
                    BlockValue reseed = BlockValue.Air;

                    // Check if block has a downgrade path
                    bool hasDowngrade = BlockValue.Air.type
                        != block.Block.DowngradeBlock.type;
                    // Always downgrade if there is a path
                    // Or reseed a replacement
                    if (DoReseed)
                    {
                        if (hasDowngrade == true)
                        {
                            reseed = block.Block.DowngradeBlock;
                        }
                        else
                        {
                            // Get explicit crop replacement for reseeding
                            string replacement = GetCropReplacement(block.Block);
                            // Fetch the BlockValue if property was found
                            if (replacement != null) reseed =
                                Block.GetBlockValue(replacement);
                        }
                        // Delay any repeated reseed actions
                        if (reseed.type != BlockValue.Air.type)
                        {
                            // Implement own timer to slow down planting
                            if (LastPlant < PlantInterval) continue;
                            LastPlant = 0; // One at a time
                        }
                    }

                    if (hasStorage)
                    {
                        // Put harvested items into bag (same tick re-use)
                        // Must execute this before trying to get a seed
                        // Yield is determined by tags from modifiers
                        // E.g. adjust yield (seeds) for downgradables
                        hasInventoryChanges |= HarvestBlockToBag(
                            block.Block, vehicle.entity.bag, random);
                        // Get the seed from the bag, if not there, use air
                        if (DecrementBagItem(vehicle.entity.bag, reseed))
                            hasInventoryChanges = true;
                        else reseed = BlockValue.Air;
                    }

                    // Replace existing block with new one
                    // Either air or a new crop seed to grow
                    _blockChangeInfo.Add(new BlockChangeInfo(
                        0, blockPos + offset, reseed));
                    // Emit the particles for each mowed item
                    if (ParticlesSys == null) continue;
                    // Emit (reduced) particles at lawn mower
                    ParticlesSys.Emit(ParticlesPerGrass / 3);
                    // Emit particles at old plant location
                    ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams
                    { position = blockPos + offset - Origin.position + particleOffset };
                    ParticlesSys.Emit(parameters, ParticlesPerGrass);
                }
            }
        }

        // Check if we have any changes to report
        if (_blockChangeInfo.Count == 0) return;
        // Pass the whole batch back to the system
        vehicle.entity.world.SetBlocksRPC(_blockChangeInfo);
        // Call sync to server (throttle?)
        if (hasInventoryChanges == false) return;
        vehicle.entity.StopUIInteraction();
    }

    private string GetCropReplacement(Block block)
    {
        // We simply match and mangle the name
        // At least that seems to have a system
        string name = block.GetBlockName();
        // Check for vanilla plant block names
        if (name.EndsWith("3HarvestPlayer"))
        {
            // Assume vanilla replacement
            return name.Substring(0,
                name.Length - 14) + "1";
        }
        // Check for DF plant block names
        else if (name.EndsWith("HarvestPlayer"))
        {
            // Assume DF replacement schema
            return name.Substring(0,
                name.Length - 13);
        }
        // Or fall-back to properties to support any plant
        else if (block.Properties.Values.TryGetString(
            "CropReplacement", out string replacement)) {
                return replacement;
        }
        // Not found
        return null;
    }

    private bool ShouldMowDown(BlockValue block)
    {
        // Check for multi-dim blocks
        // Only work on master block
        if (block.ischild) return false;
        // Do fast check for air
        if (block.isair) return false;
        // Don't mow down things that support things (cactus only?)
        if (block.Block.blockMaterial.StabilitySupport) return false;

        // Is protector installed?
        if (ProtectGrowingPlants)
        {
            // Never mow down growing plants
            // Growing plants are assumed to
            // be from players only (not wild)
            if (IsGrowingPlant(block)) return false;
        }

        // Mow down everything marked as a "plant"
        return block.Block.blockMaterial.SurfaceCategory == "plant";
    }

    private bool IsGrowingPlant(BlockValue block)
    {
        var name = block.Block.GetType().Name;
        // Check the we have a block it can grow into
        // Note: this fixes an issue with Darkness Falls
        // Note: vanilla sets `BlockPlantGrowing.nextPlant`
        // if (block.Block.UpgradeBlock.isair) return false;
        // ToDo: maybe just rely on having an upgrade block?
        return name.StartsWith("BlockPlantGrowing");
    }

    private bool IsPlayerPlant(BlockValue block)
    {
        string name = block.Block.GetBlockName();
        if (name.EndsWith("3HarvestPlayer")) return true;
        if (name.EndsWith("PlantPlayer")) return true; // DF
        return false;

    }

    // ####################################################################
    // ####################################################################

    // Helper function to get `wanted` item from `bag`, and if OK, updates `reseed`
    private bool DecrementBagItem(Bag bag, BlockValue bv)
    {
        if (bv.isair) return false;
        ItemValue iv = bv.ToItemValue();
        bool reseeded = bag.DecItem(iv, 1, true) != 0;
        // Force lawn tractor bag to display inventory changes
        OcbLawnMowing.UseBagForItemCount = bag;
        var ui = LocalPlayerUI.GetUIForPrimaryPlayer();
        var sidebar = ui?.xui?.CollectedItemList;
        sidebar?.AddItemStack(new ItemStack(iv,
            reseeded ? -1 : 0));
        OcbLawnMowing.UseBagForItemCount = null;
        return reseeded;
    }

    // ####################################################################
    // ####################################################################

    // Helper function to evaluate and collect harvest/destroy drops
    private bool HarvestBlockToBag(Block block, Bag bag, GameRandom random)
    {
        bool changes = false;
        if (bag == null) return changes;
        if (block.itemsToDrop.TryGetValue(EnumDropEvent.Harvest,
            out List<Block.SItemDropProb> harvests))
            changes |= AddDropToBag(harvests, bag, random);
        if (block.itemsToDrop.TryGetValue(EnumDropEvent.Destroy,
            out List<Block.SItemDropProb> destroys))
            changes |= AddDropToBag(destroys, bag, random);
        return changes;
    }

    // ####################################################################
    // ####################################################################

    // Helper function to evaluate probabilities for `drops` and puts them into `bag`
    // Items that don't fit into the inventory bag are lost (vanilla drops them to ground)
    private bool AddDropToBag(List<Block.SItemDropProb> drops, Bag bag, GameRandom random)
    {
        bool changes = false;
        if (bag == null) return changes;
        if (drops == null) return changes;
        if (drops.Count == 0) return changes;
        // Get reference to inform about harvest (might be expensive on CPU)
        EntityPlayerLocal player = vehicle?.entity?.GetAttachedPlayerLocal();
        // Process all entries (probability was checked)
        foreach (Block.SItemDropProb drop in drops)
        {
            // Only allow to get our harvestable items
            if (!HarvestTags.Contains(drop.tag)) continue;
            // Skip if drop has tool category we don't have
            if (!string.IsNullOrEmpty(drop.toolCategory))
                if (!HarvestTools.Contains(drop.toolCategory))
                    continue;
            // Check for optional requirements (2nd check)
            // If you want this yield as bonus for another tag
            // if (Requirements.TryGetValue(drop.tag, out var requirements))
            //     foreach (var requirement in requirements)
            //         if (!HarvestTags.Contains(requirement)) continue;
            
            // Apply overall probability if we get anything at all?
            if (random.RandomDouble > drop.prob) continue;
            // Log.Out(" has prop", drop.prob);
            // Get random count if min and max are different
            int count = drop.minCount == drop.maxCount ? drop.maxCount
                : random.RandomRange(drop.minCount, drop.maxCount + 1);
            // Log.Out(" has count", count);
            // Get effects from player (doesn't include vehicle)
            // Note: passing `vehicle.entity` would not include player
            // So one governs perk progression, the other vehicle mods
            // And we already have the vehicle mods status calculated
            float effect = EffectManager.GetValue(
                PassiveEffects.HarvestCount, null, count,
                player, tags: FastTags.Parse(drop.tag));

            // Log.Out(" has effect", effect);
            // We use rounding rules here (why not)
            count = (int)Math.Round(count * effect);
            // Adjust count by effect
            // Skip if no luck with RNG
            if (count <= 0) continue;
            // Finally try to add the items to the bag (if there is space left)
            changes |= AddItemsToBag(bag, ItemClass.GetItem(drop.name), count);
        }
        return changes;
    }

    // ####################################################################
    // ####################################################################

    // Add `count` items of `iv` to `bag`
    // Items that don't fit will be wasted!
    private bool AddItemsToBag(Bag bag, ItemValue iv, int count)
    {
        if (iv == null) return false;
        if (bag == null) return false;
        if (count <= 0) return false;
        bool hasChanges = false;
        // Get all current slots
        // ToDo: avoid too many copies
        ItemStack[] slots = bag.GetSlots();
        // Get reference to inform about harvest (might be expensive on CPU)
        EntityPlayerLocal player = vehicle?.entity?.GetAttachedPlayerLocal();
        // Sidebar is used to show the collected items in the UI (fancy, aren't we)
        var sidebar = LocalPlayerUI.GetUIForPlayer(player)?.xui?.CollectedItemList;
        // Force lawn tractor bag to display inventory changes
        OcbLawnMowing.UseBagForItemCount = bag;
        // First try existing slots with same type
        for (int i = 0; count > 0 && i < slots.Length; i += 1)
        {
            if (slots[i].itemValue.type != iv.type) continue;
            int space = iv.ItemClass.Stacknumber.Value - slots[i].count;
            if (space <= 0) continue;
            space = Utils.FastMin(space, count);
            slots[i].count += space;
            sidebar?.AddItemStack(new ItemStack(iv, space));
            hasChanges = true;
            count -= space;
        }
        // Try to add items into new slots until all is full
        for (int i = 0; count > 0 && i < slots.Length; i += 1)
        {
            if (!slots[i].IsEmpty()) continue;
            int space = iv.ItemClass.Stacknumber.Value;
            if (space <= 0) continue;
            space = Utils.FastMin(space, count);
            slots[i].itemValue = iv;
            slots[i].count = space;
            sidebar?.AddItemStack(new ItemStack(iv, space));
            hasChanges = true;
            count -= space;
        }
        OcbLawnMowing.UseBagForItemCount = null;
        if (!hasChanges) return false;
        // Update inventory
        bag.SetSlots(slots);
        // Had changes
        return true;
    }

    // ####################################################################
    // ####################################################################

}
