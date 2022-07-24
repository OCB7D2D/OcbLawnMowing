using System.Collections.Generic;
using UnityEngine;

public class VPMower : VehiclePart
{

	public bool IsOn = false;
	private float LastBlade = 0f;
	private float LastPlant = 0f;
	private float BladeInterval = 1f / 30f;
	private float PlantInterval = 1f / 8f;
	private float FuelUsePerSecond = 0.01f;
	private Vector2i Area = Vector2i.one;
	private Vector2i Reach = new Vector2i(-1, 2);

	private ParticleSystem ParticlesSys;
	private int ParticlesPerGrass = 10;

	private bool DoReseed = false;
	private bool ProtectPlayerPlants = true;

	private readonly HashSet<string> HarvestTags = new HashSet<string>();

	public override void InitPrefabConnections()
	{
		base.InitPrefabConnections();
		Transform ps = GetParticleTransform();
		if (ps == null) return; // Play safe
		ParticlesSys = ps.GetComponentInChildren<ParticleSystem>();
		if (ParticlesSys != null) ParticlesSys.Stop();
		else Log.Warning("Particle System not found");
	}

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

	// Check modifications and pre-cache some information for later
	public void UpdateModifications(ItemValue[] modifications)
	{
		HarvestTags.Clear();
		// Collect `MowerHarvestTags` from all modifiers
		foreach (ItemValue mod in modifications)
		{
			var props = mod?.ItemClass?.Properties?.Values;
			if (props != null && props.TryGetString("MowerHarvestTags", out string tags))
			{
				foreach (string tag in tags.Split(','))
					HarvestTags.Add(tag);
			}
		}
		// Cache states for faster run-time during update calls
		ProtectPlayerPlants = HarvestTags.Contains("protect");
		DoReseed = HarvestTags.Contains("reseed");
	}

	public override void HandleEvent(Event evt, VehiclePart part, float arg)
	{
		if (evt != Event.LightsOn) return;
		IsOn = arg != 0.0;
	}

	// Main function to drive the lawn mower
	public override void Update(float dt)
	{

		// Check if mower is on
		if (IsOn == false) return;

		LastBlade += dt; // add wait time
		LastPlant += dt; // add wait time
		if (LastBlade < BladeInterval) return;
		LastBlade %= BladeInterval; // reset next wait

		// Check if we have an engine and if it is running
		if (vehicle.FindPart("engine") is VPEngine engine)
			if (engine.isRunning == false) return;

		// Use up fuel to operate the blades
		if (FuelUsePerSecond > 0) vehicle.EmitEvent(
			VPFuelTank.Action.RemoveFuel,
			FuelUsePerSecond * BladeInterval);

		Vector3i blockPos = vehicle.entity.GetBlockPosition();
		List<BlockChangeInfo> _blockChangeInfo = new List<BlockChangeInfo>();

		World world = vehicle.entity.world;
		GameRandom random = world.GetGameRandom();
		bool hasStorage = vehicle.HasStorage();

		Vector3i offset = Vector3i.zero;
		for (offset.x = -Area.x; offset.x <= Area.x; offset.x++)
		{
			for (offset.z = -Area.y; offset.z <= Area.y; offset.z++)
			{
				for (offset.y = Reach.x; offset.y <= Reach.y; offset.y++)
				{
					// Get current block at position to check if mow-able
					BlockValue block = world.GetBlock(blockPos + offset);
					// Check for multi-dim blocks
					// Only work on master block
					if (block.ischild) continue;
					// Do fast check for air
					if (block.isair) continue;
					// Mushrooms are not marked `isPlant` so we use this check
					if (block.Block.blockMaterial.SurfaceCategory != "plant") continue;
					// Don't mow down things that support things (cactus only?)
					if (block.Block.blockMaterial.StabilitySupport) continue;
					// Check if we protect growing plants?
					if (ProtectPlayerPlants == true &&
						block.Block is BlockPlantGrowing) continue;
					// We simply match and mangle the name
					// At least that seems to have a system
					string bname = block.Block.GetBlockName();
					// This check is assuming a strict name schema
					bool playerPlant = bname.EndsWith("3HarvestPlayer");
					// Block to replace mowed block with
					BlockValue reseed = BlockValue.Air;
					// Check if we should reseed the plant
					// Requires a few additional checks
					if (DoReseed && hasStorage && playerPlant)
					{
						// Implement own timer to slow down farming
						if (LastPlant < PlantInterval) continue;
						LastPlant = 0; // One at a time
									   // Remove the last part from the name
									   // Trying to find block for reseeding
									   // There is no real link beside "drop" config
									   // Or maybe the "GrowInto" references, but that
									   // is the easiest check and works with name-system
						bname = bname.Substring(0, bname.Length - 14);
						BlockValue bv = Block.GetBlockValue(bname + "1");
						// Check that our assumptions are correct ;)
						if (bv.type != BlockValue.Air.type)
						{
							// Try to get seed, assigns reseed if successful
							// Otherwise we still re-seed, but with air ;)
							GetAndDecrementItem(vehicle.entity.bag, bv, ref reseed);
						}
					}
					else if (ProtectPlayerPlants && playerPlant) continue;
					// Replace existing block with new one
					// Either air or a new crop seed to grow
					_blockChangeInfo.Add(new BlockChangeInfo(
						0, blockPos + offset, reseed));
					// Emit the particles for each mowed item
					if (ParticlesSys) ParticlesSys.Emit(ParticlesPerGrass);
					// Nothing more to do if vehicle has no storage
					if (hasStorage == false) continue;
					// Otherwise put harvested items into bag
					HarvestBlockToBag(block.Block,
						vehicle.entity.bag, random);

				}
			}
		}

		// Check if we have any changes to report
		if (_blockChangeInfo.Count == 0) return;
		// Pass the whole batch back to the system
		vehicle.entity.world.SetBlocksRPC(_blockChangeInfo);
	}

	// Helper function to get `wanted` item from `bag`, and if OK, updates `reseed`
	private void GetAndDecrementItem(Bag bag, BlockValue wanted, ref BlockValue reseed)
	{
		if (wanted.type == BlockValue.Air.type) return;
		ItemValue iv = wanted.ToItemValue();
		if (bag.DecItem(iv, 1, true) == 0) return;
		reseed = wanted;
	}

	// Helper function to evaluate and collect harvest/destroy drops
	private void HarvestBlockToBag(Block block, Bag bag, GameRandom random)
	{
		if (block.itemsToDrop.TryGetValue(EnumDropEvent.Harvest,
			out List<Block.SItemDropProb> harvests))
			AddDropToBag(harvests, bag, random);
		if (block.itemsToDrop.TryGetValue(EnumDropEvent.Destroy,
			out List<Block.SItemDropProb> destroys))
			AddDropToBag(destroys, bag, random);
	}

	// Helper function to evaluate probabilities for `drops` and puts them into `bag`
	// Items that don't fit into the inventory bag are lost (vanilla drops them to ground)
	private void AddDropToBag(List<Block.SItemDropProb> drops, Bag bag, GameRandom random)
	{
		foreach (Block.SItemDropProb drop in drops)
		{
			// Only allow to get harvestable items
			if (!HarvestTags.Contains(drop.tag)) continue;
			// Apply overall probability if we get anything at all?
			if (random.RandomDouble > drop.prob) continue;
			// Get random count if min and max are different
			int count = drop.minCount == drop.maxCount ? drop.maxCount
				: random.RandomRange(drop.minCount, drop.maxCount + 1);
			// Skip if no luck with RNG
			if (count <= 0) continue;
			// Finally try to add the items to the bag (if there is space left)
			AddItemsToBag(bag, ItemClass.GetItem(drop.name), count);
		}
	}

	// Add `count` items of `iv` to `bag`
	// Items that don't fit will be wasted!
	private void AddItemsToBag(Bag bag, ItemValue iv, int count)
	{
		if (iv == null) return;
		if (bag == null) return;
		if (count <= 0) return;
		// Get all current slots
		// ToDo: avoid too many copies
		ItemStack[] slots = bag.GetSlots();
		// Get reference to inform about harvest (might be expensive on CPU)
		EntityPlayerLocal player = vehicle?.entity?.GetAttachedPlayerLocal();
		// Sidebar is used to show the collected items in the UI (fancy, aren't we)
		var sidebar = LocalPlayerUI.GetUIForPlayer(player)?.xui?.CollectedItemList;
		// First try existing slots with same type
		for (int i = 0; count > 0 && i < slots.Length; i += 1)
		{
			if (slots[i].itemValue.type != iv.type) continue;
			int space = iv.ItemClass.Stacknumber.Value - slots[i].count;
			if (space <= 0) continue;
			space = Utils.FastMin(space, count);
			sidebar?.AddItemStack(new ItemStack(iv, space));
			slots[i].count += space;
			count -= space;
		}
		// Try to add items into new slots until all is full
		for (int i = 0; count > 0 && i < slots.Length; i += 1)
		{
			if (!slots[i].IsEmpty()) continue;
			int space = iv.ItemClass.Stacknumber.Value;
			if (space <= 0) continue;
			space = Utils.FastMin(space, count);
			sidebar?.AddItemStack(new ItemStack(iv, space));
			slots[i].itemValue = iv;
			slots[i].count = space;
			count -= space;
		}
		// Update inventory
		bag.SetSlots(slots);
	}

}
