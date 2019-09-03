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

            /*Entity.*/NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
        }

        public override void UpdateOnceBeforeFrame() {

            Controller = Entity as IMyCockpit;
            NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;

        }

        private float ticks = 0;

        private Boolean fired;
        private IMyEntity targeting;

        private Vector3D targetPos;
        private int updateCount = 0;

        private Boolean waitForNew = false;
        private int wait = 0;

        public override void UpdateBeforeSimulation() {

            if (!controller) {
                return;
            }
            
            //MyLog.Default.WriteLine("executing after simulation, look here");

            ticks++;


            if (Controller.Pilot == null) {
                waitForNew = true;
            }

            if (Controller == null || Controller.MarkedForClose || Controller.Closed || !Controller.IsWorking || Controller.Pilot == null || MyAPIGateway.Gui.IsCursorVisible || MyAPIGateway.Session.Player == null || MyAPIGateway.Session.Player.Character == null || Controller.Pilot != MyAPIGateway.Session.Player.Character)
                return;

            // If cockpit does not include the following string do not allow turret control
            if (!Controller.CustomName.Contains("ATC"))
            {
                return;
            }

            // Only activate if entity is a cockpit and right mouse is pressed, or if entiy is a "TurretController" and right mouse is pressed
            if (!MyAPIGateway.Input.IsRightMousePressed() && (((IMyCockpit)Entity).BlockDefinition.TypeId == typeof(MyObjectBuilder_Cockpit))
                || !MyAPIGateway.Input.IsRightMousePressed() && Controller.BlockDefinition.SubtypeName.Contains("TurretController")
	        ) {
                return;
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
                
            var view = MyAPIGateway.Session.Camera.WorldMatrix;

            //Ray directionRay = new Ray(view2.Translation, Vector3.Normalize(view.Forward));
            Vector3D startPos = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            Vector3D startDir = Vector3.Normalize(view.Forward);
            startDir *= 2000;


            Vector3D endPos;
            //If in third person we want to raise the crosshair for easy of targeting.
            if (!Controller.IsInFirstPersonView)
            {
                Vector3 upwards = MyAPIGateway.Session.Camera.WorldMatrix.Up;
                upwards.Normalize();
                upwards *= 550;

                endPos = startPos + startDir + upwards;
            } else
            {
                endPos = startPos + startDir;
            }


            VRage.Game.ModAPI.IMyCubeGrid grid = Controller.CubeGrid;
            Vector3D target = getAimPosition(startPos, endPos, grid);
            targetPos = target;

            updateCamera();

            hasCamTarget = false;
            Sandbox.ModAPI.Ingame.IMyGridTerminalSystem GridTerminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);

            List<IMyLargeTurretBase> Turrets = new List<IMyLargeTurretBase>();

            //MyLog.Default.WriteLine("Controller name: " + Controller.CustomName.ToString());
            //GridTerminalSystem.GetBlocksOfType(Turrets);

            try {
                IMyBlockGroup group = (IMyBlockGroup) GridTerminalSystem.GetBlockGroupWithName(Controller.CustomName.ToString());
                group.GetBlocksOfType(Turrets);
                //MyLog.Default.WriteLine("found block in group!");
            } catch (NullReferenceException) {
                GridTerminalSystem.GetBlocksOfType(Turrets);
            }
            
            foreach (IMyLargeTurretBase elem in Turrets) {

                //if (elem.ToString().ToLower().Contains("exclude")) {
                if (!elem.ToString().ToLower().Contains("atc")) {
                    continue;
                }

                double azimuth = 0;
                double elevation = 0;
                Vector3D TargetLook = Vector3D.TransformNormal(elem.GetPosition() - targetPos, MatrixD.Invert(MatrixD.CreateFromDir(elem.WorldMatrix.Forward, elem.WorldMatrix.Up)));
                TargetLook.Normalize();
                TargetLook.X *= -1;
                TargetLook.Y *= -1;
                TargetLook.Z *= -1;

                Vector3D.GetAzimuthAndElevation(TargetLook, out azimuth, out elevation);

                //MyLog.Default.WriteLine("Aim global pos: " + targetPos + " targetLook=" + TargetLook + " azimuth=" + azimuth + " elevation=" + elevation);

                if ((Double.IsNaN(elevation) || Double.IsPositiveInfinity(elevation) || Double.IsNegativeInfinity(elevation))) {
                    elevation = 0F;
                }

                double maxRotationSpeed = getRotationSpeedLimit(elem.BlockDefinition.SubtypeId) * 16;
                double maxElevationSpeed = getElevationSpeedLimit(elem.BlockDefinition.SubtypeId) * 16;
                double ElevationChange, AzimuthChange;
                Boolean specialRotation = maxRotationSpeed < 0.002f || maxElevationSpeed < 0.002f;
                //specialRotation = true;
                
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

                //MyLog.Default.WriteLine("applying rotation change: " +AzimuthChange + "; " + ElevationChange );
                elem.Azimuth += (float) AzimuthChange;
                elem.Elevation += (float) ElevationChange;
                
                //float rotationSpeed = (((MyLargeTurretBase)elem)).BlockDefinition.RotationSpeed;
                //float elevationSpeed = (((MyLargeTurretBase)elem)).BlockDefinition.ElevationSpeed;

                elem.SyncAzimuth();
                elem.SyncElevation();

                Boolean atLimit = false;
                if (Math.Abs(elem.Azimuth - azimuth) > 0.05 || Math.Abs(elem.Elevation - elevation) > 0.05) {
                    atLimit = true;
                }
                
                if (fired) {
                    //check if ship is not in the way
                    //Boolean notintersecting = TestScript.TestScript.targetIsFine(elem, grid, targetPos);
                    Boolean notintersecting = targetIsFine(elem, grid, targetPos);

                    if (notintersecting && !atLimit) {
                        elem.ApplyAction("ShootOnce");
                    }

                }
            }
        }

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

            //VRageRender.MyRenderProxy.DebugDrawLine3D(from, to, Color.White, Color.OrangeRed, false);

            List<VRage.Game.ModAPI.IHitInfo> hits = new List<VRage.Game.ModAPI.IHitInfo>();
            MyAPIGateway.Physics.CastRay(from, to, hits);
            //MyLog.Default.WriteLine("raycast from: " + from + " to=" + to + " hitcount:" + hits.Count);

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


            /*if (hasCamTarget) {
                drawAt = targetPos - (targetPos - startPos) * 0.2;
            }*/
            
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
            //MyLog.Default.WriteLine("executing before simulation");

            if (Controller == null || Controller.MarkedForClose || Controller.Closed || !Controller.IsWorking)
                return;


            if (updateCount == 0) {
                return;
            }

            updateCount--;

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

        private static IMyEntity getAimEntity(Vector3D from, Vector3D to, VRage.Game.ModAPI.IMyCubeGrid grid) {


            List<VRage.Game.ModAPI.IHitInfo> hits = new List<VRage.Game.ModAPI.IHitInfo>();
            MyAPIGateway.Physics.CastRay(from, to, hits);

            if (hits.Count > 0) {
                foreach (VRage.Game.ModAPI.IHitInfo hit in hits) {
                    //MyLog.Default.WriteLine("Entity aim name (entity search): " + hit.HitEntity.ToString());
                    
                    if (hit.HitEntity.ToString().Contains("Grid") && hit.HitEntity.EntityId != grid.EntityId) {
                        //MyLog.Default.WriteLine("targeting grid! (entity search)");
                        return hit.HitEntity;
                    }
                }
            }

            return null;
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
