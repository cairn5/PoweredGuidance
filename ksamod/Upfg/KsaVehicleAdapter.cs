using System;
using System.Collections.Generic;
using KSA;

namespace PoweredGuidance.Upfg;

// Builds the UPFG stage list from the controlled vehicle's live KSA part tree.
//
// KSA orders in-flight events by *sequences* (PartTree.SequenceList): activating the
// next sequence fires the IActivate modules (EngineController ignitions, Decoupler
// separations) on that sequence's parts. We replay the not-yet-activated sequences
// over the current part tree, tracking which parts remain attached and which engines
// are lit, and emit one UPFG stage (Mode 1, constant vacuum thrust) per powered phase.
//
// Mass model (serial staging): a powered phase runs until the next separation, and
// the propellant it burns is whatever is left in the parts that separation drops;
// the final phase burns everything still aboard. Dry mass per part is the sum of its
// InertMass modules; propellant is the live substance mass of its tanks. These are
// the same two terms KSA itself sums for Vehicle.TotalMass, so the stage masses stay
// consistent with the mass the guidance loop is fed each step.
public static class KsaVehicleAdapter
{
    private const double G0 = 9.80665;

    public static UpfgVehicle Build(Vehicle vehicle)
    {
        var result = new UpfgVehicle();
        PartTree tree = vehicle.Parts;
        if (tree == null || tree.Moles == null || tree.SequenceList == null)
            return result;

        // Per-part masses from the live tree. SubtreeModules covers a part's
        // sub-parts too, and every module belongs to exactly one full part, so
        // summing over tree parts counts each module exactly once.
        var attached = new HashSet<Part>();
        var dryMass = new Dictionary<Part, double>();
        var propMass = new Dictionary<Part, double>();
        var engines = new HashSet<EngineController>();

        ReadOnlySpan<MoleState> moleStates = tree.Moles.States;
        ReadOnlySpan<Part> parts = tree.Parts;
        for (int i = 0; i < parts.Length; i++)
        {
            Part part = parts[i];
            attached.Add(part);

            double dry = 0;
            Span<InertMass> inert = part.SubtreeModules.Get<InertMass>();
            for (int j = 0; j < inert.Length; j++)
                dry += inert[j].MassPropertiesAsmb.Props.Mass;
            dryMass[part] = dry;

            double prop = 0;
            Span<Tank> tanks = part.SubtreeModules.Get<Tank>();
            for (int j = 0; j < tanks.Length; j++)
                prop += tanks[j].ComputeSubstanceMass(moleStates);
            propMass[part] = prop;

            Span<EngineController> ecs = part.SubtreeModules.Get<EngineController>();
            for (int j = 0; j < ecs.Length; j++)
                if (ecs[j].IsActive)
                    engines.Add(ecs[j]);
        }

        double wet = 0;
        foreach (Part p in attached)
            wet += dryMass[p] + propMass[p];

        // Replay the remaining sequences in activation order (the list is kept
        // sorted by SequenceList.ResetCaches).
        ReadOnlySpan<Sequence> sequences = tree.SequenceList.Sequences;
        for (int i = 0; i < sequences.Length; i++)
        {
            Sequence seq = sequences[i];
            if (seq.Activated)
                continue;

            var ignited = new List<EngineController>();
            var detachRoots = new List<Part>();
            ReadOnlySpan<Part> seqParts = seq.Parts;
            for (int j = 0; j < seqParts.Length; j++)
            {
                Part p = seqParts[j];
                if (!attached.Contains(p))
                    continue;

                Span<EngineController> ecs = p.SubtreeModules.Get<EngineController>();
                for (int k = 0; k < ecs.Length; k++)
                    ignited.Add(ecs[k]);

                Span<Decoupler> decs = p.SubtreeModules.Get<Decoupler>();
                for (int k = 0; k < decs.Length; k++)
                {
                    Part root = DetachedRoot(decs[k]);
                    if (root != null && attached.Contains(root))
                        detachRoots.Add(root);
                }
            }

            if (detachRoots.Count > 0)
            {
                var dropped = new HashSet<Part>();
                foreach (Part root in detachRoots)
                    CollectSubtree(root, attached, dropped);

                double droppedProp = 0;
                foreach (Part p in dropped)
                    droppedProp += propMass[p];

                (double thrust, double flow) = SumThrust(engines, attached);
                if (thrust > 0 && flow > 0 && droppedProp > 0)
                {
                    // The phase ending at this separation burns the propellant of
                    // the parts it is about to drop.
                    result.Stages.Add(MakeStage(thrust, flow, wet, wet - droppedProp));
                    wet -= droppedProp;
                    foreach (Part p in dropped)
                        propMass[p] = 0;
                }

                // Jettison structure plus any propellant that was never burned.
                foreach (Part p in dropped)
                {
                    wet -= dryMass[p] + propMass[p];
                    attached.Remove(p);
                }
                engines.RemoveWhere(e => !attached.Contains(e.Parent.FullPart));
            }

            foreach (EngineController e in ignited)
                if (attached.Contains(e.Parent.FullPart))
                    engines.Add(e);
        }

        // Final phase: burn everything still aboard.
        double remaining = 0;
        foreach (Part p in attached)
            remaining += propMass[p];
        (double finalThrust, double finalFlow) = SumThrust(engines, attached);
        if (finalThrust > 0 && finalFlow > 0 && remaining > 0)
            result.Stages.Add(MakeStage(finalThrust, finalFlow, wet, wet - remaining));

        return result;
    }

    // The part whose subtree separates when this decoupler fires: the tree-child
    // side of the decoupler's connection — the same rule Vehicle.Split applies.
    private static Part DetachedRoot(Decoupler decoupler)
    {
        Part.Connection conn = decoupler.Connector?.Connection;
        if (conn == null)
            return null;
        Part a = conn.Connectors[0].ConnectionPart;
        Part b = conn.Connectors[1].ConnectionPart;
        if (a == null || b == null)
            return null;
        return !a.TreeChildren.Contains(b) ? a : b;
    }

    private static void CollectSubtree(Part root, HashSet<Part> attached, HashSet<Part> dropped)
    {
        if (!attached.Contains(root) || !dropped.Add(root))
            return;
        foreach (Part child in root.TreeChildren)
            CollectSubtree(child, attached, dropped);
    }

    private static (double thrust, double flow) SumThrust(
        HashSet<EngineController> engines, HashSet<Part> attached)
    {
        double thrust = 0, flow = 0;
        foreach (EngineController e in engines)
        {
            if (!attached.Contains(e.Parent.FullPart))
                continue;
            thrust += e.VacuumData.ThrustMax.Length();
            flow += e.VacuumData.MassFlowRateMax;
        }
        return (thrust, flow);
    }

    private static UpfgStage MakeStage(double thrust, double flow, double wet, double dry)
    {
        return new UpfgStage
        {
            Mode = 1,
            Thrust = thrust,
            Isp = thrust / flow / G0,
            MassTotal = wet,
            MassDry = Math.Max(dry, 1.0),
            GLim = 1e9,
        };
    }
}
