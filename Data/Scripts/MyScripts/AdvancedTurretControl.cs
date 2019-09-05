using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Gui;
using Sandbox.Game.Weapons;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRage.Game;
using VRage.Game.Components;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;


namespace Rearth.AdvancedTurretControl {

    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Cockpit), false)]
    public class AdvancedTurretController : MyGameLogicComponent {

        private Boolean controller = false;

        private IMyCockpit Controller;
        public override void Init(MyObjectBuilder_EntityBase objectBuilder) {
		
            if (((IMyCockpit)Entity).BlockDefinition.TypeId == typeof(MyObjectBuilder_Cockpit)) {
                controller = true;
            } else {
                return;
            }

            NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame() {
            Controller = Entity as IMyCockpit;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }

        private Boolean fired;
        private IMyEntity targeting;

        private Vector3D targetPos;

        private Boolean waitForNew = false;
        private int wait = 0;

        public override void UpdateBeforeSimulation() {

            // Only run script on valid, working, piloted cockpits.
            if (!controller ||
                Controller == null ||
                Controller.MarkedForClose ||
                Controller.Closed ||
                !Controller.IsWorking ||
                Controller.Pilot == null ||
                MyAPIGateway.Gui.IsCursorVisible ||
                MyAPIGateway.Session.Player == null ||
                MyAPIGateway.Session.Player.Character == null ||
                Controller.Pilot != MyAPIGateway.Session.Player.Character ||
                // If cockpit does not include the following string do not allow turret control
                !Controller.CustomName.Contains("ATC") ||
                // Only activate if Right Mouse is pressed
                (!MyAPIGateway.Input.IsRightMousePressed() && (((IMyCockpit)Entity).BlockDefinition.TypeId == typeof(MyObjectBuilder_Cockpit)))
            ) {
                return;
            }

            // I'm not quite sure if the following two if statements do anything useful. They may help with performance.
            // If pilot is not present, wait 60 ticks before checking again 
            if (Controller.Pilot == null) {
                waitForNew = true;
            }
            if (!waitForNew) {
                fired = MyAPIGateway.Input.IsLeftMousePressed();
            } else {
                wait++;
                fired = false;
                if (wait > 60) {
                    waitForNew = false;
                    wait = 0;
                }
            }
                
            //Get current camera position in space
            Vector3D startPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;

            //Acting as though our camera with current rotation is located at 0:0:0,
            // add 2000m in the foward direction and save this new cordinate as forwards.
            Vector3D forwards = MyAPIGateway.Session.Camera.WorldMatrix.Forward;
            forwards.Normalize();
            forwards *= 2000;
            
            //Acting as though our camera with current rotation is located at 0:0:0,
            // add 550m in the upward direction and save this new cordinate as forwards.
            Vector3 upwards = MyAPIGateway.Session.Camera.WorldMatrix.Up;
            upwards.Normalize();
            upwards *= 550;

            //We now can add our forwards cordinates to our startPos to get a targeting position.
            // When in third person having the target directly infront of our view camera position
            // can be a bit hard to aim. We can move this up by adding the upwards cordinate as well.
            Vector3D endPos;
            if (!Controller.IsInFirstPersonView) {
                endPos = startPos + forwards + upwards;
            } else {
                endPos = startPos + forwards;
            }

            //Finally take this endPos and check if there is a target along the between the startPos 
            // and endPos. If a grid is detected, set targetPos.
            VRage.Game.ModAPI.IMyCubeGrid grid = Controller.CubeGrid;
            Vector3D target = getAimPosition(startPos, endPos, grid);
            targetPos = target;

            //Draw crosshair overlays now that we know location of endPos and targetPos
            updateCamera();

            hasCamTarget = false;
            Sandbox.ModAPI.Ingame.IMyGridTerminalSystem GridTerminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);

            //Check if there is a group with same name as current cockpit. If so we will only
            // controll those turrets, otherwise control all turrets.
            List<IMyLargeTurretBase> Turrets = new List<IMyLargeTurretBase>();
            try {
                IMyBlockGroup group = (IMyBlockGroup) GridTerminalSystem.GetBlockGroupWithName(Controller.CustomName.ToString());
                group.GetBlocksOfType(Turrets);
            } catch (NullReferenceException) {
                GridTerminalSystem.GetBlocksOfType(Turrets);
            }
            
            foreach (IMyLargeTurretBase elem in Turrets) {

                //Only control turret if it contains the string "atc" and is not currently under user control
                if ((!elem.ToString().ToLower().Contains("atc")) || (elem.IsUnderControl)) {
                    continue;
                }

                //Get current rotation speeds
                double azimuth = 0;
                double elevation = 0;
                Vector3D TargetLook = Vector3D.TransformNormal(elem.GetPosition() - targetPos, MatrixD.Invert(MatrixD.CreateFromDir(elem.WorldMatrix.Forward, elem.WorldMatrix.Up)));
                TargetLook.Normalize();
                TargetLook.X *= -1;
                TargetLook.Y *= -1;
                TargetLook.Z *= -1;

                Vector3D.GetAzimuthAndElevation(TargetLook, out azimuth, out elevation);

                //Zero out elevation of not valid number
                if ((Double.IsNaN(elevation) || Double.IsPositiveInfinity(elevation) || Double.IsNegativeInfinity(elevation))) {
                    elevation = 0F;
                }

                //Lookup turret from list of known turrets and set max rotation speed
                double maxRotationSpeed = getRotationSpeedLimit(elem.BlockDefinition.SubtypeId) * 16;
                double maxElevationSpeed = getElevationSpeedLimit(elem.BlockDefinition.SubtypeId) * 16;
                double ElevationChange, AzimuthChange;
                Boolean specialRotation = maxRotationSpeed < 0.002f || maxElevationSpeed < 0.002f;

                //Using max speeds calculate rotation speed
                if (specialRotation) {
                    if (Math.Abs(azimuth - elem.Azimuth) > maxRotationSpeed) {
                        if (azimuth - elem.Azimuth >= 0) {
                            AzimuthChange = maxRotationSpeed;
                        } else {
                            AzimuthChange = -maxRotationSpeed;
                        }
                    } else {
                        AzimuthChange = azimuth - elem.Azimuth;
                    }

                    if (Math.Abs(elevation - elem.Elevation) > maxElevationSpeed) {
                        if (elevation - elem.Elevation >= 0) {
                            ElevationChange = maxElevationSpeed;
                        } else {
                            ElevationChange = -maxElevationSpeed;
                        }
                    } else {
                        ElevationChange = elevation - elem.Elevation;
                    }
                } else {
                    AzimuthChange = azimuth - elem.Azimuth;
                    ElevationChange = elevation - elem.Elevation;
                }

                //Apply calculated rotation speeds
                elem.Azimuth += (float) AzimuthChange;
                elem.Elevation += (float) ElevationChange;

                elem.SyncAzimuth();
                elem.SyncElevation();

                //Check if rotation speed is at limit
                Boolean atLimit = false;
                if (Math.Abs(elem.Azimuth - azimuth) > 0.05 || Math.Abs(elem.Elevation - elevation) > 0.05) {
                    atLimit = true;
                }
                
                //Check if our ship is not in the way. If clear shoot.
                if (fired) {
                    Boolean notintersecting = targetIsFine(elem, grid, targetPos);

                    if (notintersecting && !atLimit) {
                        elem.ApplyAction("ShootOnce");
                    }

                }
            }
        }

        //Function to check if targeting own grid, only fire if not own grid.
        public static Boolean targetIsFine(IMyLargeTurretBase elem, VRage.Game.ModAPI.IMyCubeGrid grid, Vector3D targetPos) {
            var gun = elem as IMyGunObject<Sandbox.Game.Weapons.MyGunBase>;
            var matrix = gun.GunBase.GetMuzzleWorldMatrix();

            var from = matrix.Translation;
            var to = targetPos - from;
            to.Normalize();
            to.X *= 200;
            to.Y *= 200;
            to.Z *= 200;
            to += from;

            List<VRage.Game.ModAPI.IHitInfo> hits = new List<VRage.Game.ModAPI.IHitInfo>();
            MyAPIGateway.Physics.CastRay(from, to, hits);

            if (hits.Count > 0) {
                //MyLog.Default.WriteLine(" first hit=" + hits[0].HitEntity.ToString() + " own grid=" + grid.ToString());
                if (hits[0].HitEntity.EntityId.Equals(grid.EntityId)) {
                    return false;
                }
                return true;
            }
            return true;
        }

        Boolean hasCamTarget = false;

        private void updateCamera(IMyEntity target) {
            if (target == null) {
                return;
            }

            hasCamTarget = true;

            var Material = MyStringId.GetOrCompute("Crosshair");
            float width = (float)target.WorldAABB.Extents.X;
            float height = (float)target.WorldAABB.Extents.Z;
            BoundingBoxD box = target.WorldAABB;
            box.Translate(target.WorldMatrix);
            Vector3D center = target.WorldAABB.Min;
            center.X += box.HalfExtents.X;
            center.Y += box.HalfExtents.Y;
            center.Z += box.HalfExtents.Z;

            try {
                MyTransparentGeometry.AddBillboardOriented(Material, Color.WhiteSmoke.ToVector4(), center, MyAPIGateway.Session.Camera.WorldMatrix.Left, MyAPIGateway.Session.Camera.WorldMatrix.Up, height, BlendTypeEnum.SDR);
            } catch (NullReferenceException ex) {
                //MyLog.Default.Error("Error while drawing billboard: " + ex);
            }
                //MyLog.Default.WriteLine("Drawing Billbord at: " + center);
        }

        private void updateCamera() {
            Vector3D startPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            Vector3 dirA = targetPos - startPos;
            float distance = dirA.Length();
            dirA.Normalize();
            //MyLog.Default.WriteLine("Data: " + (targetPos - startPos)); 
            //drawAt = targetPos;

            float scale;
            float dis;

            if (distance < 150) {
                scale = 2.5f;
                dis = 50f;
            } else {
                scale = 7.5f;
                dis = 150f;
            }
            
            Vector3D drawAt = startPos + dirA * dis;

            var Material = MyStringId.GetOrCompute("Crosshair_Center");
            try {
                if (hasCamTarget) {
                    MyTransparentGeometry.AddBillboardOriented(Material, Color.Red.ToVector4(), drawAt, MyAPIGateway.Session.Camera.WorldMatrix.Left, MyAPIGateway.Session.Camera.WorldMatrix.Up, scale, BlendTypeEnum.SDR);
                } else {
                
                    MyTransparentGeometry.AddBillboardOriented(Material, Color.White.ToVector4(), drawAt, MyAPIGateway.Session.Camera.WorldMatrix.Left, MyAPIGateway.Session.Camera.WorldMatrix.Up, scale, BlendTypeEnum.SDR);
                }
            } catch (NullReferenceException ex) {
                //MyLog.Default.Error("Error while drawing billboard: " + ex);
            }
           hasCamTarget = false;
        }


        public override void UpdateAfterSimulation() {
            if (!controller) {
                return;
            }

            if (Controller == null || Controller.MarkedForClose || Controller.Closed || !Controller.IsWorking)
                return;

            VRage.Game.ModAPI.IMyCubeGrid grid = Controller.CubeGrid;
            Sandbox.ModAPI.Ingame.IMyGridTerminalSystem GridTerminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
        }

        private Vector3D getAimPosition(Vector3D from, Vector3D to, VRage.Game.ModAPI.IMyCubeGrid grid) {
            List<VRage.Game.ModAPI.IHitInfo> hits = new List<VRage.Game.ModAPI.IHitInfo>();
            MyAPIGateway.Physics.CastRay(from, to, hits);

            if (hits.Count > 0) {
                foreach (VRage.Game.ModAPI.IHitInfo hit in hits) {
                    //MyLog.Default.WriteLine("Entity aim name: " + hit.HitEntity.ToString());
                    if (hit.HitEntity.ToString().Contains("Grid") && hit.HitEntity.EntityId != grid.EntityId) {
                        //MyLog.Default.WriteLine("targeting grid!");
                        updateCamera(hit.HitEntity);
                        return hit.Position;
                    }
                }
            }

            return to;
        }

        public static float getRotationSpeedLimit(string subTypeId) {
            //PlasmaTurret
            switch (subTypeId) {
                case "BattleshipCannonMK2":
                    return 0.00012f;
                case "BattleshipCannonMK22":
                    return 0.00012f;
                case "BattleshipCannonMK3":
                    return 0.00010f;
                case "TelionAMACM":
                    return 0.00240f;
                case "TelionAF":
                    return 0.00045f;
                case "TelionAF_small":
                    return 0.00035f;
                case "TelionAFGen2":
                    return 0.00080f;
                case "BFTriCannon":
                    return 0.00008f;
                case "BFG_M":
                    return 0.00015f;
                case "HeavyDefenseTurret":
                    return 0.00028f;
                case "BattleshipCannon":
                    return 0.00014f;
                default:
                    return 0.02f;
            }
        }
        public static float getElevationSpeedLimit(string subTypeId) {

            switch (subTypeId) {
                case "BattleshipCannonMK2":
                    return 0.00014f;
                case "BattleshipCannonMK22":
                    return 0.00014f;
                case "BattleshipCannonMK3":
                    return 0.00012f;
                case "TelionAMACM":
                    return 0.00240f;
                case "TelionAF":
                    return 0.00010f;
                case "TelionAF_small":
                    return 0.00008f;
                case "TelionAFGen2":
                    return 0.0018f;
                case "BFTriCannon":
                    return 0.00015f;
                case "BFG_M":
                    return 0.00010f;
                case "HeavyDefenseTurret":
                    return 0.00036f;
                case "BattleshipCannon":
                    return 0.00016f;
                default:
                    return 0.02f;
            }
        }

    }
}
