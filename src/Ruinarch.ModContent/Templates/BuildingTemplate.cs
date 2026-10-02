using System;
using System.Collections.Generic;

namespace Ruinarch.ModContent.Templates
{
	/// <summary>
	/// The look of one building, as a template pack stores it (<c>templates/*.json</c>, see
	/// docs/TEMPLATES.md). Cells are the building's own tilemap cells; positions are in the
	/// building's local units (one unit is one tile). Layers are rows of palette keys, top
	/// row first; '.' is an empty cell.
	/// </summary>
	public sealed class BuildingTemplate
	{
		public int formatVersion = 1;
		/// <summary>Stable id, <c>pack-id/template-id</c>; part of the pool name saves record.</summary>
		public string id;
		public string name;
		/// <summary>A game STRUCTURE_TYPE name (TAVERN) or a ModContent structure id.</summary>
		public string kind;
		/// <summary>FACTION_TYPE names the look is used for.</summary>
		public List<string> cultures = new List<string>();
		/// <summary>RESOURCE name (WOOD, STONE, ...).</summary>
		public string material;
		/// <summary>Game prefab the hidden settings come from; null: the kind's own for the culture.</summary>
		public string behavesLike;
		/// <summary>The prefab's _size: w, h.</summary>
		public int[] size;
		/// <summary>The prefab's _center: x, y, z (a tilemap cell).</summary>
		public int[] center;
		/// <summary>x, y, w, h of the painted cells (tilemap cells).</summary>
		public int[] bounds;
		/// <summary>One-character key to a tile: <c>game:name</c> or <c>art:file.png</c>.</summary>
		public Dictionary<string, string> palette = new Dictionary<string, string>();
		public List<string> floor = new List<string>();
		public List<string> detail = new List<string>();
		public List<string> walls = new List<string>();
		public List<TemplateThinWall> thinWalls = new List<TemplateThinWall>();
		public List<TemplateObject> objects = new List<TemplateObject>();
		public List<TemplatePoint> entrances = new List<TemplatePoint>();
		public List<TemplatePoint> lightSpots = new List<TemplatePoint>();
		/// <summary>Each room is a list of cells (x, y, z).</summary>
		public List<List<int[]>> rooms = new List<List<int[]>>();
		/// <summary>Occupied cells when they differ from the floor cells; null: the floor cells.</summary>
		public List<int[]> footprint;
		/// <summary>The click area; null: the footprint's bounding box.</summary>
		public TemplateBox clickBox;
	}

	/// <summary>A marker (entrance, ward light spot) at a local position x, y, z.</summary>
	public sealed class TemplatePoint
	{
		public float[] pos;
	}

	/// <summary>A preplaced object: TILE_OBJECT_TYPE name, local position, rotation (degrees), sprite.</summary>
	public sealed class TemplateObject
	{
		public string type;
		public float[] pos;
		public float rot;
		public string sprite;
	}

	/// <summary>A thin wall piece: local position, rotation, its sprite renderers' sprites in order.</summary>
	public sealed class TemplateThinWall
	{
		public float[] pos;
		public float rot;
		/// <summary>Optional stock wall layout: prefab name plus '#' and wall index.</summary>
		public string layout;
		public List<string> sprites = new List<string>();
	}

	/// <summary>The click collider's offset and size, in its own local units.</summary>
	public sealed class TemplateBox
	{
		public float[] offset;
		public float[] size;
	}

	/// <summary>A template that cannot be read, validated or built; the message says why.</summary>
	public sealed class TemplateException : Exception
	{
		public TemplateException(string message) : base(message)
		{
		}
	}
}
