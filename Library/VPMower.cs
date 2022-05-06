using System.Collections.Generic;

public class VPMower : VehiclePart
{

	public bool IsOn = false;
	private float waited = 0f;
	private float interval = 0.1f;

	private Vector2i area = Vector2i.one;
	private Vector2i reach = Vector2i.zero;

	public override void SetProperties(DynamicProperties properties)
	{
		if (properties.Contains("area")) area = StringParsers
				.ParseVector2i(properties.GetString("area"));
		if (properties.Contains("reach")) reach = StringParsers
				.ParseVector2i(properties.GetString("reach"));
	}

	public override void HandleEvent(Event evt, VehiclePart part, float arg)
	{
		if (evt != Event.LightsOn) return;
		IsOn = arg != 0.0;
	}

	public override void Update(float dt)
    {

		// Check if mower is on
		if (IsOn == false) return;

		waited += dt; // add wait time
		if (waited < interval) return;
		waited %= interval; // reset next wait

		Vector3i blockPos = vehicle.entity.GetBlockPosition();
		List<BlockChangeInfo> _blockChangeInfo = new List<BlockChangeInfo>();

		Vector3i offset = Vector3i.zero;
		for (offset.x = -area.x; offset.x <= area.x; offset.x++)
		{
			for (offset.z = -area.y; offset.z <= area.y; offset.z++)
			{
				for (offset.y = reach.x; offset.y <= reach.y; offset.y++)
				{
					// Get current block at position
					BlockValue block1 = vehicle.entity.world
						.GetBlock(blockPos + offset);
					// Mow everything away for now :-)
					if (block1.Block.blockMaterial.IsPlant)
					{
						_blockChangeInfo.Add(new BlockChangeInfo(
							0, blockPos + offset, BlockValue.Air));
					}
				}
			}
		}

		if (_blockChangeInfo.Count == 0) return;
		vehicle.entity.world.SetBlocksRPC(_blockChangeInfo);
    }

}
