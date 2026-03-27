var langs = { "truc": ["1", "2"] };
const keys = langs.Keys();
for (let i = 0; i < langs.lenght(); i++)
{
    const ul = keys[i];
    for (let j = 0; j < keys.lenght(); j++)
    {
        ul.appendChild(langs[keys[j]]);
    }

    document.appendChild(ul);

}