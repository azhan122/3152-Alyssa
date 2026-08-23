using System;
using UnityEngine;

/// <summary>
/// A double precision position in the infinite world.
/// <para>
/// Unity's <see cref="Vector3"/> is 32-bit and starts losing centimetre precision a few
/// kilometres out, which is why <see cref="World_Origin"/> keeps the scene near zero. The
/// true position lives here instead: <c>world = originOffset + localPosition</c>. Generation,
/// saving and clue targets should all speak in these, never in raw transform positions.
/// </para>
/// </summary>
[Serializable]
public struct World_Coord : IEquatable<World_Coord>
{
	public double X;
	public double Y;
	public double Z;

	public static readonly World_Coord Zero = new(0d, 0d, 0d);

	public World_Coord(double x, double y, double z)
	{
		X = x;
		Y = y;
		Z = z;
	}

	public World_Coord(Vector3 position) : this(position.x, position.y, position.z) { }

	/// <summary>Offsets by a scene-space vector, the normal way to turn a local position into a world one.</summary>
	public static World_Coord operator +(World_Coord coord, Vector3 offset)
		=> new(coord.X + offset.x, coord.Y + offset.y, coord.Z + offset.z);

	public static World_Coord operator -(World_Coord coord, Vector3 offset)
		=> new(coord.X - offset.x, coord.Y - offset.y, coord.Z - offset.z);

	/// <summary>
	/// Difference between two world positions as a float vector. Only safe when the two are
	/// reasonably close, which is the case for anything inside the streamed area.
	/// </summary>
	public static Vector3 operator -(World_Coord a, World_Coord b)
		=> new((float)(a.X - b.X), (float)(a.Y - b.Y), (float)(a.Z - b.Z));

	public static bool operator ==(World_Coord a, World_Coord b) => a.Equals(b);

	public static bool operator !=(World_Coord a, World_Coord b) => !a.Equals(b);

	/// <summary>Flat distance, the one that matters for sailing and island streaming.</summary>
	public double FlatDistanceTo(World_Coord other)
	{
		double dx = X - other.X;
		double dz = Z - other.Z;

		return Math.Sqrt(dx * dx + dz * dz);
	}

	/// <summary>
	/// The chunk this position falls in. Uses floor rather than truncation so chunks stay a
	/// uniform grid across the origin instead of doubling up around zero.
	/// </summary>
	public Vector2Int ToChunk(float chunkSize)
		=> new((int)Math.Floor(X / chunkSize), (int)Math.Floor(Z / chunkSize));

	public bool Equals(World_Coord other) => X == other.X && Y == other.Y && Z == other.Z;

	public override bool Equals(object obj) => obj is World_Coord other && Equals(other);

	public override int GetHashCode() => HashCode.Combine(X, Y, Z);

	public override string ToString() => $"({X:F1}, {Y:F1}, {Z:F1})";
}
