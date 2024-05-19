using System.Collections.Generic;
using UnityEngine;

// The code here is not really used or ready to be used!
// Left in as a good starting point if I ever finish it!

public class ItemActionScythe : ItemAction
{

    // ####################################################################
    // ####################################################################
    
    public class ItemActionMowing : ItemActionAttackData
    {
        public PerlinNoise MeanderNoise;
        public float lastAccuracy;
        public float distance;

        public ItemActionMowing(ItemInventoryData _invData, int _indexInEntityOfAction)
            : base(_invData, _indexInEntityOfAction)
        {
            MeanderNoise = new PerlinNoise(_invData.holdingEntity.entityId + _invData.item.Id);
        }
    }

    // ####################################################################
    // ####################################################################

    public override void ExecuteAction(ItemActionData _actionData, bool _bReleased)
    {
        if (_bReleased)
        {
            if (_actionData.bWaitForRelease)
            {
                _actionData.bWaitForRelease = false;
            }
            // _actionData.bFirstHitInARow = true;
        }
        else if (!_actionData.bWaitForRelease)
        {
            // Wait at least some time before doing next action?
            if (Time.time - _actionData.lastUseTime < Delay) return;
            // Update the timestamp right after check
            _actionData.lastUseTime = Time.time;
            _actionData.bWaitForRelease = true;
            _actionData.invData.holdingEntity.RightArmAnimationAttack = true;
        }
    }

    // ####################################################################
    // ####################################################################

    protected virtual void hitTheTarget(
      ItemActionData action,
      WorldRayHitInfo hitInfo)
    {
        ItemInventoryData invData = action.invData;
        if (!invData.hitInfo.bHitValid) return;
        if (!GameUtils.IsBlockOrTerrain(invData.hitInfo.tag)) return;

        Vector3i blockPos = invData.hitInfo.hit.blockPos;
        List<BlockChangeInfo> _blockChangeInfo = new List<BlockChangeInfo>();
        // Vector3 position = _actionData.invData.holdingEntity.GetPosition();
        int clrIdx = invData.hitInfo.hit.clrIdx;

        Vector3i offset = Vector3i.zero;

        for (offset.x = -2; offset.x <= 2; offset.x++)
        {
            for (offset.z = -2; offset.z <= 2; offset.z++)
            {
                for (offset.y = -1; offset.y <= 1; offset.y++)
                {
                    // Get current block at position
                    BlockValue block1 = invData.world
                        .GetBlock(clrIdx, blockPos + offset);
                    // Mow everything away for now :-)
                    if (block1.Block.blockMaterial.IsPlant)
                    {
                        _blockChangeInfo.Add(new BlockChangeInfo(
                            clrIdx, blockPos + offset, BlockValue.Air));
                    }
                }
            }
        }

        invData.world.SetBlocksRPC(_blockChangeInfo);
    }

    // ####################################################################
    // ####################################################################
    
    public override void OnHoldingUpdate(ItemActionData _actionData)
    {

        if (!_actionData.bWaitForRelease) return;
        EntityAlive holdingEntity = _actionData.invData.holdingEntity;
        if (!holdingEntity.IsAttackValid())
        {
            Log.Out("Attack Not Valid?");
            return;
        }
        if (GetExecuteActionTarget(_actionData) is WorldRayHitInfo hitInfo)
        {
            hitTheTarget(_actionData, hitInfo);
            holdingEntity.AddStamina(-1);
        }
    }

    // ####################################################################
    // ####################################################################
    
    public override bool isShowOverlay(ItemActionData actionData)
    {
        return true;
    }

    public override bool IsActionRunning(ItemActionData _actionData)
    {
        // Log.Out("IsActionRunning");
        return _actionData.HasExecuted;
    }

    // ####################################################################
    // ####################################################################

    //public override bool GrazeCast(
    //  ItemActionDynamic.ItemActionDynamicData _actionData,
    //  float normalizedClipTime = -1f)
    //{
    //	Log.Out("GrazeCast");
    //	return false;
    //}
    //
    //public override bool Raycast(
    //ItemActionDynamic.ItemActionDynamicData _actionData)
    //{
    //	Log.Out("Raycast");
    //	return false;
    //}

    // ####################################################################
    // ####################################################################

    public override ItemActionData CreateModifierData(
      ItemInventoryData _invData, int _indexInEntityOfAction)
    {
        return new ItemActionMowing(_invData, _indexInEntityOfAction); ;
    }

    public override ItemClass.EnumCrosshairType GetCrosshairType(ItemActionData _actionData) => ItemClass.EnumCrosshairType.Plus;

    // ####################################################################
    // ####################################################################

    //public override void StopHolding(ItemActionData _data)
    //{
    //	Log.Out("StopHolding");
    //}
    //
    //public override void StartHolding(ItemActionData _data)
    //{
    //	Log.Out("StartHolding");
    //}

    // ####################################################################
    // ####################################################################

    public override bool AllowConcurrentActions() => false;

    // ####################################################################
    // ####################################################################
    
    public override void ReadFrom(DynamicProperties _props)
    {
        base.ReadFrom(_props);
        _props.ParseFloat("Range", ref Range);
    }

    // ####################################################################
    // ####################################################################
    
    protected virtual Vector3 getDirectionOffset(
      ItemActionMowing action,
      Vector3 _forward,
      int _shotOffset = 0)
    {
        float num1 = EffectManager.GetValue(PassiveEffects.SpreadDegreesHorizontal, action.invData.itemValue, 45f, action.invData.holdingEntity) * action.lastAccuracy * ((float)action.MeanderNoise.Noise(Time.time, 0.0, _shotOffset) * 0.66f);
        double num2 = EffectManager.GetValue(PassiveEffects.SpreadDegreesVertical, action.invData.itemValue, 45f, action.invData.holdingEntity) * action.lastAccuracy * (action.MeanderNoise.Noise(0.0, Time.time, _shotOffset) * 0.660000026226044);
        Vector3 vector3 = Quaternion.Euler((float)num2, (float)num1, 0.0f) * Vector3.forward;
        return Quaternion.LookRotation(_forward, Vector3.up) * vector3;
    }

    // ####################################################################
    // ####################################################################
    
    public virtual float GetRange(ItemActionData _actionData) => EffectManager.GetValue(PassiveEffects.MaxRange, _actionData.invData.itemValue, this.Range, _actionData.invData.holdingEntity);

    // ####################################################################
    // ####################################################################
    
    public override WorldRayHitInfo GetExecuteActionTarget(ItemActionData action)
    {
        ItemActionMowing mowing = (ItemActionMowing)action;
        Ray lookRay = action.invData.holdingEntity.GetLookRay();
        lookRay.direction = getDirectionOffset(mowing, lookRay.direction);
        float range = GetRange(action);
        mowing.distance = range;
        int modelLayer = action.invData.holdingEntity.GetModelLayer();
        action.invData.holdingEntity.SetModelLayer(2);
        bool hasHit = Voxel.Raycast(action.invData.world,
            lookRay, range, -538750997, 8, 0.0f);
        action.invData.holdingEntity.SetModelLayer(modelLayer);
        if (hasHit)
        {
            WorldRayHitInfo updatedHitInfo = action.GetUpdatedHitInfo();
            mowing.distance = Mathf.Sqrt(updatedHitInfo.hit.distanceSq);
            return updatedHitInfo;
        }
        return null;
    }

    // ####################################################################
    // ####################################################################

}
