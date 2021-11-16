﻿using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using static UnityEngine.GraphicsBuffer;

namespace VFEMech
{
    public class TeslaChainingProps : DefModExtension
    {
        public bool addFire;
        public float range;
        public int bounceCount;
        public DamageDef damageDef;
        public DamageDef explosionDamageDef;
        public float impactRadius;
        public bool targetFriendly;
        public int lifetime;
    }
    public class TeslaProjectile : Bullet
    {
        public int curLifetime;
        private int numBounces;
        private List<TeslaProjectile> allProjectiles = new List<TeslaProjectile>();
        private List<Thing> prevTargets = new List<Thing>();
        private Thing holder;
        private Thing mainLauncher;
        private bool firedOnce;
        public Thing Holder
        {
            get
            {
                if (holder == null)
                {
                    return this.launcher;
                }
                return holder;
            }
        }

        protected override void Impact(Thing hitThing)
        {
            if (this.mainLauncher == null)
            {
                this.mainLauncher = this.launcher;
            }
            if (hitThing != null && !firedOnce)
            {
                hitThing.TakeDamage(new DamageInfo(Props.damageDef, this.def.projectile.GetDamageAmount(1f), -1f, Holder.DrawPos.AngleToFlat(hitThing.DrawPos), this.Launcher));
                if (Props.addFire && hitThing.TryGetComp<CompAttachBase>() != null)
                {
                    var fire = (Fire)GenSpawn.Spawn(ThingDefOf.Fire, hitThing.Position, hitThing.Map);
                    fire.AttachTo(hitThing);
                }
                if (Props.impactRadius > 0f)
                {
                    GenExplosion.DoExplosion(hitThing.Position, Map, Props.impactRadius, Props.explosionDamageDef, this.Launcher, this.def.projectile.GetDamageAmount(1f));
                }
                RegisterHit(hitThing);
                if (numBounces < Props.bounceCount)
                {
                    var target = NextTarget();
                    if (target != null)
                    {
                        FireAt(target);
                    }
                }
                firedOnce = true;
            }

            base.Impact(hitThing);
        }

        private void RegisterHit(Thing hitThing)
        {
            RegisterHit(this, hitThing);
            foreach (var projectile in allProjectiles)
            {
                RegisterHit(projectile, hitThing);
            }
        }

        private void RegisterHit(TeslaProjectile projectile, Thing hitThing)
        {
            if (!projectile.prevTargets.Contains(hitThing))
            {
                projectile.prevTargets.Add(hitThing);
            }
            projectile.curLifetime = 0;
        }

        public TeslaChainingProps Props => def.GetModExtension<TeslaChainingProps>();

        public Thing PrimaryEquipment
        {
            get
            {
                var launcher = PrimaryLauncher;
                if (launcher is Building_TurretGun turretGun)
                {
                    return turretGun.gun;
                }
                return null;
            }
        }
        public override void Draw()
        {
            var vec1 = Holder.DrawPos;
            var vec2 = this.DrawPos;
            if (vec2.magnitude > vec1.magnitude)
            {
                var t = vec1;
                vec1 = vec2;
                vec2 = t;
            }

            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(vec2 + (vec1 - vec2) / 2, Quaternion.AngleAxis(vec1.AngleToFlat(vec2) + 90f, Vector3.up), new Vector3(1f, 1f, (vec1 - vec2).magnitude)),
                Graphic.MatSingle, 0);
        }
        public void FireAt(Thing target)
        {
            var projectile = (TeslaProjectile)GenSpawn.Spawn(this.def, Position, Map);
            projectile.Launch(launcher, target, target, this.HitFlags, false, PrimaryEquipment);
            projectile.holder = this;
            if (this.mainLauncher != null)
            {
                projectile.mainLauncher = this.mainLauncher;
            }

            allProjectiles.Add(projectile);
            prevTargets.Add(target);
            if (projectile.prevTargets is null)
            {
                projectile.prevTargets = new List<Thing>();
            }
            projectile.prevTargets.AddRange(prevTargets);
            projectile.numBounces = numBounces;
            projectile.curLifetime = curLifetime;
            numBounces++;
        }

        private Thing NextTarget()
        {
            var things = GenRadial.RadialDistinctThingsAround(Holder.Position, Map, Props.range, false)
                .Where(t => (Props.targetFriendly || t.HostileTo(this.launcher))).Except(new[] { this, usedTarget.Thing });
            things = things.Except(prevTargets);
            things = things.OrderBy(t => t.Position.DistanceTo(Holder.Position));
            var target = things.FirstOrDefault();
            return target;
        }

        private Thing PrimaryLauncher
        {
            get
            {
                if (this.mainLauncher != null)
                {
                    return this.mainLauncher;
                }
                foreach (var projectile in this.allProjectiles)
                {
                    if (projectile.mainLauncher != null)
                    {
                        return projectile.mainLauncher;
                    }
                }
                return null;
            }
        }

        public override void Tick()
        {
            base.Tick();
            this.curLifetime++;
            if (curLifetime > Props.lifetime)
            {
                Log.Message("Destroy 1");
                DestroyAll();
            }
            else if (this.Holder.Destroyed)
            {
                Log.Message("Destroy 2");
                DestroyAll();
            }
            else if (allProjectiles.Any(x => x.Destroyed))
            {
                Log.Message("Destroy 3");
                DestroyAll();
            }
            //else
            //{
            //    var primaryLauncher = PrimaryLauncher;
            //    if (primaryLauncher is Building_TurretGun turretGun && turretGun.AttackVerb.state == VerbState.Idle)
            //    {
            //        Log.Message("Destroy 4");
            //        DestroyAll();
            //    }
            //}

        }
        public void DestroyAll()
        {
            destroyAll = true;
            for (var i = allProjectiles.Count - 1; i >= 0; i--)
            {
                if (!allProjectiles[i].Destroyed)
                {
                    allProjectiles[i].Destroy();
                }
            }
            this.Destroy();
            destroyAll = false;
        }

        public static bool destroyAll;
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (destroyAll)
            {
                base.Destroy(mode);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref mainLauncher, "mainLauncher");
            Scribe_References.Look(ref holder, "holder");
            Scribe_Values.Look(ref numBounces, "numBounces");
            Scribe_Values.Look(ref curLifetime, "curLifetime");
            Scribe_Values.Look(ref firedOnce, "firedOnce");
            Scribe_Collections.Look(ref allProjectiles, "allProjectiles", LookMode.Reference);
            Scribe_Collections.Look(ref prevTargets, "prevTargets", LookMode.Reference);
        }
    }

}
