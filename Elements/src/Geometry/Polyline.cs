using Elements.Geometry.Interfaces;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using ClipperLib;
using System.Linq;

namespace Elements.Geometry
{
    /// <summary>
    /// A continuous set of lines.
    /// </summary>
    /// <example>
    /// [!code-csharp[Main](../../Elements/test/PolylineTests.cs?name=example)]
    /// </example>
    public partial class Polyline : ICurve, IEquatable<Polyline>
    {
        /// <summary>
        /// Calculate the length of the polygon.
        /// </summary>
        public override double Length()
        {
            var length = 0.0;
            for (var i = 0; i < this.Vertices.Count - 1; i++)
            {
                length += this.Vertices[i].DistanceTo(this.Vertices[i + 1]);
            }
            return length;
        }

        /// <summary>
        /// The start of the polyline.
        /// </summary>
        [JsonIgnore]
        public Vector3 Start
        {
            get { return this.Vertices[0]; }
        }

        /// <summary>
        /// The end of the polyline.
        /// </summary>
        [JsonIgnore]
        public Vector3 End
        {
            get { return this.Vertices[this.Vertices.Count - 1]; }
        }

        /// <summary>
        /// Reverse the direction of a polyline.
        /// </summary>
        /// <returns>Returns a new polyline with opposite winding.</returns>
        public Polyline Reversed()
        {
            var revVerts = new List<Vector3>(this.Vertices);
            revVerts.Reverse();
            return new Polyline(revVerts);
        }

        /// <summary>
        /// Get a string representation of this polyline.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Join<Vector3>(",", this.Vertices);
        }

        /// <summary>
        /// Get a collection a lines representing each segment of this polyline.
        /// </summary>
        /// <returns>A collection of Lines.</returns>
        public virtual Line[] Segments()
        {
            return SegmentsInternal(this.Vertices);
        }

        /// <summary>
        /// Get a point on the polygon at parameter u.
        /// </summary>
        /// <param name="u">A value between 0.0 and 1.0.</param>
        /// <returns>Returns a Vector3 indicating a point along the Polygon length from its start vertex.</returns>
        public override Vector3 PointAt(double u)
        {
            var segmentIndex = 0;
            var p = PointAtInternal(u, out segmentIndex);
            return p;
        }

        /// <summary>
        /// Get the Transform at the specified parameter along the Polygon.
        /// </summary>
        /// <param name="u">The parameter on the Polygon between 0.0 and 1.0.</param>
        /// <returns>A Transform with its Z axis aligned trangent to the Polygon.</returns>
        public override Transform TransformAt(double u)
        {
            if (u < 0.0 || u > 1.0)
            {
                throw new ArgumentOutOfRangeException($"The provided value for u ({u}) must be between 0.0 and 1.0.");
            }

            var segmentIndex = 0;
            var o = PointAtInternal(u, out segmentIndex);
            Vector3 x = Vector3.XAxis; // Vector3: Convert to XAxis

            // Check if the provided parameter is equal
            // to one of the vertices.
            Vector3 a = new Vector3();
            var isEqualToVertex = false;
            foreach (var v in this.Vertices)
            {
                if (v.Equals(o))
                {
                    isEqualToVertex = true;
                    a = v;
                }
            }

            var normals = this.NormalsAtVertices();

            if (isEqualToVertex)
            {
                var idx = this.Vertices.IndexOf(a);

                if (idx == 0 || idx == this.Vertices.Count - 1)
                {
                    return CreateOrthogonalTransform(idx, a, normals[idx]);
                }
                else
                {
                    return CreateMiterTransform(idx, a, normals[idx]);
                }
            }

            var d = this.Length() * u;
            var totalLength = 0.0;
            var segments = Segments();
            var normal = new Vector3();
            for (var i = 0; i < segments.Length; i++)
            {
                var s = segments[i];
                var currLength = s.Length();
                if (totalLength <= d && totalLength + currLength >= d)
                {
                    var parameterOnSegment = (d - totalLength) / currLength;
                    o = s.PointAt(parameterOnSegment);
                    var previousNormal = normals[i];
                    var nextNormal = normals[(i + 1) % this.Vertices.Count];
                    normal = ((nextNormal - previousNormal) * parameterOnSegment + previousNormal).Unitized();
                    x = s.Direction().Cross(normal);
                    break;
                }
                totalLength += currLength;
            }
            return new Transform(o, x, normal, x.Cross(normal));
        }

        /// <summary>
        /// Construct a transformed copy of this Polyline.
        /// </summary>
        /// <param name="transform">The transform to apply.</param>
        public Polyline TransformedPolyline(Transform transform)
        {
            var transformed = new Vector3[this.Vertices.Count];
            for (var i = 0; i < transformed.Length; i++)
            {
                transformed[i] = transform.OfPoint(this.Vertices[i]);
            }
            var p = new Polyline(transformed);
            return p;
        }

        /// <summary>
        /// Construct a transformed copy of this Curve.
        /// </summary>
        /// <param name="transform">The transform to apply.</param>
        public override Curve Transformed(Transform transform)
        {
            return TransformedPolyline(transform);
        }

        /// <summary>
        /// Transform a specified segment of this polyline in place.
        /// </summary>
        /// <param name="t">The transform. If it is not within the polygon plane, then an exception will be thrown.</param>
        /// <param name="i">The segment to transform. If it does not exist, then no work will be done.</param>
        /// <param name="isClosed">If set to true, the segment between the start end end point will be considered a valid target.</param>
        /// <param name="isPlanar">If set to true, an exception will be thrown if the resultant shape is no longer planar.</param>
        public void TransformSegment(Transform t, int i, bool isClosed = false, bool isPlanar = false)
        {
            var v = this.Vertices;

            if (i < 0 || i > v.Count)
            {
                // Segment index is out of range, do no work.
                return;
            }

            var candidates = new List<Vector3>(this.Vertices);

            var endIndex = (i + 1) % v.Count;

            candidates[i] = t.OfPoint(v[i]);
            candidates[endIndex] = t.OfPoint(v[endIndex]);

            // All motion for a triangle results in a planar shape, skip this case.
            var enforcePlanar = v.Count != 3 && isPlanar;

            if (enforcePlanar && !candidates.AreCoplanar())
            {
                throw new Exception("Segment transformation must be within the polygon's plane.");
            }

            this.Vertices = candidates;
        }

        /// <summary>
        /// Get the normal of each vertex on the polyline.
        /// </summary>
        /// <returns>A collection of unit vectors, each corresponding to a single vertex.</returns>
        protected virtual Vector3[] NormalsAtVertices()
        {
            // Vertex normals will be the cross product of the previous edge and the next edge.
            var nextDirection = (this.Vertices[1] - this.Vertices[0]).Unitized();

            // At the first point, use either the next non-collinear edge or a cardinal direction to choose a normal.
            var previousDirection = new Vector3();
            if (Vector3Extensions.AreCollinear(this.Vertices))
            {
                // If the polyline is collinear, use whichever cardinal direction isn't collinear with it.
                if (Math.Abs(nextDirection.Dot(Vector3.YAxis)) < 1 - Vector3.EPSILON)
                {
                    previousDirection = Vector3.YAxis;
                }
                else
                {
                    previousDirection = Vector3.XAxis;
                }
            }
            else
            {
                // Find the next non-collinear edge and use that to hint the normal of the first vertex.
                for (var i = 2; i < this.Vertices.Count; i++)
                {
                    previousDirection = (this.Vertices[i] - this.Vertices[1]).Unitized();
                    if (Math.Abs(previousDirection.Dot(nextDirection)) > 1 - Vector3.EPSILON)
                    {
                        break;
                    }
                }
            }

            // Create an array of transforms with the same number of items as the vertices.
            var result = new Vector3[this.Vertices.Count];
            var previousNormal = new Vector3();
            for (var i = 0; i < result.Length; i++)
            {
                // If this vertex has a bend, use the normal computed from the previous and next edges.
                // Otherwise keep using the normal frnom the previous bend.
                if (i < result.Length - 1)
                {
                    var direction = (this.Vertices[i + 1] - this.Vertices[i]).Unitized();
                    if (Math.Abs(nextDirection.Dot(direction)) < 1 - Vector3.EPSILON)
                    {
                        previousDirection = nextDirection;
                        nextDirection = direction;
                    }
                }
                var normal = nextDirection.Cross(previousDirection);

                // Flip the normal if it's pointing away from the previous point's normal.
                if (i > 1 && previousNormal.Dot(normal) < 0)
                {
                    normal *= -1;
                }
                result[i] = normal.Unitized();
                previousNormal = normal;
            }
            return result;
        }

        /// <summary>
        /// Get the transforms used to transform a Profile extruded along this Polyline.
        /// </summary>
        /// <param name="startSetback"></param>
        /// <param name="endSetback"></param>
        public override Transform[] Frames(double startSetback, double endSetback)
        {
            var normals = this.NormalsAtVertices();

            // Create an array of transforms with the same number of items as the vertices.
            var result = new Transform[this.Vertices.Count];
            for (var i = 0; i < result.Length; i++)
            {
                var a = this.Vertices[i];
                result[i] = CreateOrthogonalTransform(i, a, normals[i]);
            }
            return result;
        }

        /// <summary>
        /// Get the bounding box for this curve.
        /// </summary>
        public override BBox3 Bounds()
        {
            return new BBox3(this.Vertices);
        }

        /// <summary>
        /// Compute the Plane defined by the first three non-collinear vertices of the Polygon.
        /// </summary>
        /// <returns>A Plane.</returns>
        public Plane Plane()
        {
            var xform = Vertices.ToTransform();
            return xform.OfPlane(new Plane(Vector3.Origin, Vector3.ZAxis));
        }

        /// <summary>
        /// A list of vertices describing the arc for rendering.
        /// </summary>
        internal override IList<Vector3> RenderVertices()
        {
            return this.Vertices;
        }

        /// <summary>
        /// Check for coincident vertices in the supplied vertex collection.
        /// </summary>
        /// <param name="vertices"></param>
        protected void CheckCoincidenceAndThrow(IList<Vector3> vertices)
        {
            for (var i = 0; i < vertices.Count; i++)
            {
                for (var j = 0; j < vertices.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }
                    if (vertices[i].IsAlmostEqualTo(vertices[j]))
                    {
                        throw new ArgumentException($"The polyline could not be created. Two vertices were almost equal: {i} {vertices[i]} {j} {vertices[j]}.");
                    }
                }
            }
        }

        /// <summary>
        /// Check if any of the polygon segments have zero length.
        /// </summary>
        internal static void CheckSegmentLengthAndThrow(IList<Line> segments)
        {
            foreach (var s in segments)
            {
                if (s.Length() == 0)
                {
                    throw new ArgumentException("A segment fo the polyline has zero length.");
                }
            }
        }

        /// <summary>
        /// Check for self-intersection in the supplied line segment collection.
        /// </summary>
        /// <param name="t">The transform representing the plane of the polygon.</param>
        /// <param name="segments"></param>
        internal static void CheckSelfIntersectionAndThrow(Transform t, IList<Line> segments)
        {
            var segmentsTrans = new List<Line>();

            foreach (var l in segments)
            {
                segmentsTrans.Add(l.TransformedLine(t));
            };

            for (var i = 0; i < segmentsTrans.Count; i++)
            {
                for (var j = 0; j < segmentsTrans.Count; j++)
                {
                    if (i == j)
                    {
                        // Don't check against itself.
                        continue;
                    }

                    if (segmentsTrans[i].Intersects2D(segmentsTrans[j]))
                    {
                        throw new ArgumentException($"The polyline could not be created. Segments {i} and {j} intersect.");
                    }
                }
            }
        }

        internal static Line[] SegmentsInternal(IList<Vector3> vertices)
        {
            var result = new Line[vertices.Count - 1];
            for (var i = 0; i < vertices.Count - 1; i++)
            {
                var a = vertices[i];
                var b = vertices[i + 1];
                result[i] = new Line(a, b);
            }
            return result;
        }

        /// <summary>
        /// Generates a transform that expresses the plane of a miter join at a point on the curve.
        /// </summary>
        protected Transform CreateMiterTransform(int i, Vector3 a, Vector3 up)
        {
            var b = i == 0 ? this.Vertices[this.Vertices.Count - 1] : this.Vertices[i - 1];
            var c = i == this.Vertices.Count - 1 ? this.Vertices[0] : this.Vertices[i + 1];
            var l1 = (a - b).Unitized();
            var l2 = (c - a).Unitized();
            var x1 = l1.Cross(up);
            var x2 = l2.Cross(up);
            var x = x1.Average(x2);
            return new Transform(this.Vertices[i], x, x.Cross(up));
        }

        private Transform CreateOrthogonalTransform(int i, Vector3 a, Vector3 up)
        {
            Vector3 b, x, c;

            if (i == 0)
            {
                b = this.Vertices[i + 1];
                var z = (a - b).Unitized();
                return new Transform(a, up.Cross(z), z);
            }
            else if (i == this.Vertices.Count - 1)
            {
                b = this.Vertices[i - 1];
                var z = (b - a).Unitized();
                return new Transform(a, up.Cross(z), z);
            }
            else
            {
                b = this.Vertices[i - 1];
                c = this.Vertices[i + 1];
                var v1 = (b - a).Unitized();
                var v2 = (c - a).Unitized();
                x = v1.Average(v2).Negate();
                return new Transform(this.Vertices[i], x, x.Cross(up));
            }
        }

        /// <summary>
        /// Get a point on the polygon at parameter u.
        /// </summary>
        /// <param name="u">A value between 0.0 and 1.0.</param>
        /// <param name="segmentIndex">The index of the segment containing parameter u.</param>
        /// <returns>Returns a Vector3 indicating a point along the Polygon length from its start vertex.</returns>
        protected virtual Vector3 PointAtInternal(double u, out int segmentIndex)
        {
            if (u < 0.0 || u > 1.0)
            {
                throw new Exception($"The value of u ({u}) must be between 0.0 and 1.0.");
            }

            var d = this.Length() * u;
            var totalLength = 0.0;
            for (var i = 0; i < this.Vertices.Count - 1; i++)
            {
                var a = this.Vertices[i];
                var b = this.Vertices[i + 1];
                var currLength = a.DistanceTo(b);
                var currVec = (b - a);
                if (totalLength <= d && totalLength + currLength >= d)
                {
                    segmentIndex = i;
                    return a + currVec * ((d - totalLength) / currLength);
                }
                totalLength += currLength;
            }
            segmentIndex = this.Vertices.Count - 1;
            return this.End;
        }

        /// <summary>
        /// Offset this polyline by the specified amount.
        /// </summary>
        /// <param name="offset">The amount to offset.</param>
        /// <param name="endType">The closure type to use on the offset polygon.</param>
        /// <param name="tolerance">An optional tolerance.</param>
        /// <returns>A new closed Polygon offset in all directions by offset from the polyline.</returns>
        public virtual Polygon[] Offset(double offset, EndType endType, double tolerance = Vector3.EPSILON)
        {
            var clipperScale = 1.0 / tolerance;
            var path = this.ToClipperPath(tolerance);

            var solution = new List<List<IntPoint>>();
            var co = new ClipperOffset();
            ClipperLib.EndType clEndType;
            switch (endType)
            {
                case EndType.Butt:
                    clEndType = ClipperLib.EndType.etOpenButt;
                    break;
                case EndType.ClosedPolygon:
                    clEndType = ClipperLib.EndType.etClosedPolygon;
                    break;
                case EndType.Square:
                default:
                    clEndType = ClipperLib.EndType.etOpenSquare;
                    break;
            }
            co.AddPath(path, JoinType.jtMiter, clEndType);
            co.Execute(ref solution, offset * clipperScale);  // important, scale also used here

            var result = new Polygon[solution.Count];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = solution[i].ToPolygon(tolerance);
            }
            return result;
        }

        /// <summary>
        /// Does this polyline equal the provided polyline?
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(Polyline other)
        {
            if (this.Vertices.Count != other.Vertices.Count)
            {
                return false;
            }
            for (var i = 0; i < Vertices.Count; i++)
            {
                if (!this.Vertices[i].Equals(other.Vertices[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Identify any shared segments between two polylines.
        /// </summary>
        /// <param name="a">The first polyline to compare.</param>
        /// <param name="b">The second polyline to compare.</param>
        /// <param name="isClosed">Flag as closed to include segment between first and last vertex.</param>
        /// <returns>Returns a list of tuples of indices for the segments that match in each polyline.</returns>
        public static List<(int indexOnA, int indexOnB)> SharedSegments(Polyline a, Polyline b, bool isClosed = false)
        {
            var result = new List<(int, int)>();

            // Abbreviate lists to compare
            var va = a.Vertices;
            var vb = b.Vertices;

            for (var i = 0; i < va.Count; i++)
            {
                var ia = va[i];
                var ib = va[(i + 1) % va.Count];

                var iterations = isClosed ? vb.Count : vb.Count - 1;

                for (var j = 0; j < iterations; j++)
                {
                    var ja = vb[j];

                    if (ia.IsAlmostEqualTo(ja))
                    {
                        // Current vertices match, compare next vertices
                        var jNext = (j + 1) % vb.Count;
                        var jPrev = j == 0 ? vb.Count - 1 : j - 1;

                        var jb = vb[jNext];
                        var jc = vb[jPrev];

                        if (ib.IsAlmostEqualTo(jb))
                        {
                            // Match is current segment a and current segment b
                            result.Add((i, j));
                        }

                        if (ib.IsAlmostEqualTo(jc))
                        {
                            // Match is current segment a and previous segment b
                            result.Add((i, jPrev));
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Close the polyline to create a polygon.
        /// </summary>
        /// <returns>A polygon.</returns>
        public Polygon Closed()
        {
            var vertices = new List<Vector3>(this.Vertices);
            if (vertices[vertices.Count - 1].IsAlmostEqualTo(vertices[0]))
            {
                return new Polygon(vertices.Skip(1).ToList());
            }
            else
            {
                return new Polygon(vertices);
            }
        }
    }

    /// <summary>
    /// Polyline extension methods.
    /// </summary>
    internal static class PolylineExtensions
    {
        /// <summary>
        /// Construct a clipper path from a Polygon.
        /// </summary>
        /// <param name="p"></param>
        /// <param name="tolerance">An optional tolerance. If converting back to a Polyline, be sure to use the same tolerance.</param>
        /// <returns></returns>
        internal static List<IntPoint> ToClipperPath(this Polyline p, double tolerance = Vector3.EPSILON)
        {
            var clipperScale = Math.Round(1.0 / tolerance);
            var path = new List<IntPoint>();
            foreach (var v in p.Vertices)
            {
                path.Add(new IntPoint(Math.Round(v.X * clipperScale), Math.Round(v.Y * clipperScale)));
            }
            return path;
        }

        /// <summary>
        /// Convert a line to a polyline
        /// </summary>
        /// <param name="l">The line to convert.</param>
        public static Polyline ToPolyline(this Line l) => new Polyline(new[] { l.Start, l.End });

        internal class Node
        {
            public Vector3 Position { get; set; }
            public Node[] Children { get; set; }
            public bool Visited { get; set; }

            public Node(Vector3 position)
            {
                this.Position = position;
                this.Children = new Node[2];
            }

            public override bool Equals(object obj)
            {
                if (!(obj is Node))
                {
                    return false;
                }

                var n = (Node)obj;
                return n.Position.IsAlmostEqualTo(this.Position) && n.Children[0].Equals(this.Children[0]) && n.Children[1].Equals(this.Children[1]);
            }

            public override int GetHashCode()
            {
                int hashCode = -236938580;
                hashCode = hashCode * -1521134295 + Position.GetHashCode();
                hashCode = hashCode * -1521134295 + EqualityComparer<Node[]>.Default.GetHashCode(Children);
                return hashCode;
            }
        }

        /// <summary>
        /// Create polylines from a collection of line segments.
        /// </summary>
        /// <param name="lines">A collection of lines.</param>
        /// <returns>A collection of polylines.</returns>
        public static List<Polyline> ToPolylines(this List<Line> lines)
        {
            var nodes = new List<Node>();

            // Construct a graph where each node has two children.
            foreach (var segment in lines)
            {
                var start = nodes.FirstOrDefault(n => n.Position.IsAlmostEqualTo(segment.Start));
                if (start == null)
                {
                    start = new Node(segment.Start);
                    nodes.Add(start);
                }

                var end = nodes.FirstOrDefault(n => n.Position.IsAlmostEqualTo(segment.End));
                if (end == null)
                {
                    end = new Node(segment.End);
                    nodes.Add(end);
                }

                if (start.Children[0] != null)
                {
                    start.Children[1] = end;
                }
                else
                {
                    start.Children[0] = end;
                }

                if (end.Children[0] != null)
                {
                    end.Children[1] = start;
                }
                else
                {
                    end.Children[0] = start;
                }
            }

            var polylines = new List<Polyline>();

            foreach (var node in nodes)
            {
                if (node.Visited)
                {
                    continue;
                }

                var verts = new List<Vector3>();
                var child = node;
                while (!child.Visited)
                {
                    child.Visited = true;

                    verts.Add(child.Position);

                    if (child.Children != null)
                    {
                        if (child.Children[0] != null && !child.Children[0].Visited)
                        {
                            child = child.Children[0];
                        }
                        else if (child.Children[1] != null && !child.Children[1].Visited)
                        {
                            child = child.Children[1];
                        }
                        else
                        {
                            // Everything has been visited.
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                polylines.Add(new Polyline(verts));
            }

            return polylines;
        }
    }

    /// <summary>
    /// Offset end types
    /// </summary>
    public enum EndType
    {
        /// <summary>
        /// Open ends are extended by the offset distance and squared off
        /// </summary>
        Square,
        /// <summary>
        /// Ends are squared off with no extension
        /// </summary>
        Butt,
        /// <summary>
        /// If open, ends are joined and treated as a closed polygon
        /// </summary>
        ClosedPolygon,
    }
}
