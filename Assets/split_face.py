import os

import bpy
from mathutils import Vector


FACE_OBJECT_NAME = "Plane"
MOUTH_CUT_Z = 1.61


def clip_polygon(points, keep_above):
    clipped = []
    for index, current in enumerate(points):
        previous = points[index - 1]
        current_inside = current.z >= MOUTH_CUT_Z if keep_above else current.z <= MOUTH_CUT_Z
        previous_inside = previous.z >= MOUTH_CUT_Z if keep_above else previous.z <= MOUTH_CUT_Z

        if current_inside != previous_inside:
            amount = (MOUTH_CUT_Z - previous.z) / (current.z - previous.z)
            clipped.append(previous.lerp(current, amount))
        if current_inside:
            clipped.append(current.copy())

    return clipped


def make_piece(face_object, keep_above):
    vertices = []
    faces = []
    vertex_indices = {}

    def add_vertex(point):
        key = tuple(round(component, 7) for component in point)
        if key not in vertex_indices:
            vertex_indices[key] = len(vertices)
            vertices.append(tuple(point))
        return vertex_indices[key]

    for polygon in face_object.data.polygons:
        world_points = [
            face_object.matrix_world @ face_object.data.vertices[index].co
            for index in polygon.vertices
        ]
        clipped = clip_polygon(world_points, keep_above)
        if len(clipped) < 3:
            continue

        face = [add_vertex(point) for point in clipped]
        if len(set(face)) >= 3:
            faces.append(face)

    return vertices, faces


def write_obj(path, object_name, vertices, faces):
    with open(path, "w", encoding="utf-8", newline="\n") as output:
        output.write("# Blender world coordinates; converted to Unity at runtime.\n")
        output.write("o " + object_name + "\n")
        for vertex in vertices:
            output.write("v {:.7f} {:.7f} {:.7f}\n".format(*vertex))
        for face in faces:
            output.write("f " + " ".join(str(index + 1) for index in face) + "\n")


face_object = bpy.data.objects.get(FACE_OBJECT_NAME)
if face_object is None or face_object.type != "MESH":
    raise RuntimeError("The Blender scene must contain the face mesh named 'Plane'.")

project_root = os.path.abspath(os.path.join(bpy.path.abspath("//"), "..", ".."))
output_directory = os.path.join(project_root, "Assets", "StreamingAssets")
os.makedirs(output_directory, exist_ok=True)

upper_vertices, upper_faces = make_piece(face_object, keep_above=True)
jaw_vertices, jaw_faces = make_piece(face_object, keep_above=False)
if not upper_faces or not jaw_faces:
    raise RuntimeError("The mouth cut did not produce both face pieces.")

write_obj(
    os.path.join(output_directory, "head_top.obj"),
    "Upper_Head",
    upper_vertices,
    upper_faces,
)
write_obj(
    os.path.join(output_directory, "head_jaw.obj"),
    "Lower_Jaw",
    jaw_vertices,
    jaw_faces,
)

print(
    "Exported Blender-aligned mask: "
    "{} upper vertices, {} jaw vertices; cut Z={:.3f}.".format(
        len(upper_vertices), len(jaw_vertices), MOUTH_CUT_Z
    )
)
