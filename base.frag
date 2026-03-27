#version 330 core
in vec4 fColor;
in vec2 fTexCoords;

out vec4 FragColor;

uniform sampler2D uTexture;

void main()
{
    // On multiplie la couleur de la texture par la couleur du sprite (qui contient déjà la teinte debug/matérial)
    FragColor = texture(uTexture, fTexCoords) * fColor;
}