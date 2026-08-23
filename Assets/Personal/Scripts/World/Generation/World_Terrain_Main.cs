using EditorAttributes;
using UnityEngine;

public class World_Terrain_Main : MonoBehaviour
{
    public Transform plane;
    public MeshFilter islandMeshFilter;
    public Vector2 position;
    public int noiseScale = 1;
    public float heightScale = 1;

    public int width = 10;
    public int height = 10;
    Mesh mesh;
    Vector3[] vertices;

    void Update()
    {
        Regenerate();
    }

    public void Regenerate()
    {
        mesh = islandMeshFilter.mesh;
        vertices = islandMeshFilter.mesh.vertices;

        for (float y = 0.0F; y < height; y++)
        {
            for (float x = 0.0F; x < width; x++)
            {
                float xCoord = position.x + x /  width * noiseScale;
                float yCoord = position.y + y /  height * noiseScale;
                float sample = Mathf.PerlinNoise(xCoord, yCoord);

                int index = (int)(y * width + x);
                Vector3 v = vertices[index];
                v.y = sample * heightScale;
                print(v.y);
                vertices[index] = v;
            }
        }

        mesh.vertices = vertices;
        islandMeshFilter.mesh = mesh;
        islandMeshFilter.mesh.RecalculateNormals();
    }
}
