# Prettier x64

[![Build status](https://ci.appveyor.com/api/projects/status/ygik32a9bco8l2kv?svg=true)](https://ci.appveyor.com/project/kennethscott/prettierx64)

Download this extension from the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=vs-publisher-126251.PrettierX64)

---------------------------------------

Prettier is an opinionated code formatter. It enforces a consistent style by parsing your code and re-printing it with its own rules that take the maximum line length into account, wrapping code when necessary.

This extension was originally forked from Mads Kristensen's JavaScript Prettier.  It was renamed, refactored, and updated for Visual Studio 2022+ x64 compatibility. It also
now supports any file types supported by Prettier.

See the [change log](CHANGELOG.md) for changes and road map.

## Features

- Prettifies JavaScript, TypeScript, JSON, CSS/SCSS/LESS, HTML, Markdown, etc.  
- Uses [Prettier](https://github.com/jlongster/prettier) node module
    - If a version of Prettier can be found installed via npm locally (relative to the current file), it will be used.  This means if you have extensions
      configured, it will use them and you can add more file types.
    - If no local Prettier installation is found, the extension falls back to an embedded Prettier.
- Reads the standard [Prettier configuration file](https://prettier.io/docs/en/configuration.html)

### Prettify
This extension calls the [Prettier](https://github.com/jlongster/prettier) node module behind the scenes to format any supported file type to its standards.

For example, take the following code:

```js
foo(arg1, arg2, arg3, arg4);
```

That looks like the right way to format it. However, we've all run
into this situation:

```js
foo(reallyLongArg(), omgSoManyParameters(), IShouldRefactorThis(), isThereSeriouslyAnotherOne());
```

Suddenly our previous format for calling function breaks down because
this is too long. What you would probably do is this instead:

```js
foo(
  reallyLongArg(),
  omgSoManyParameters(),
  IShouldRefactorThis(),
  isThereSeriouslyAnotherOne()
);
```

Invoke the command from the context menu in the editor or via hotkey CTRL + K + J.  

![Context Menu](art/context-menu.png)

### FAQ

#### Configuration via .prettierrc
It is quite easy to setup Prettier to format a little bit differently, like having 4 spaces instead of 2 spaces per tab. 

The easiest way is to [create a `.prettierrc`](https://prettier.io/docs/en/configuration.html) file in your project root. 

Here is an example containing the two most common settings that people want to change: `tabWidth` is how many spaces it uses for indentation, and `printWidth` is how long a line can be before it breaks down:

```json
  {
    "tabWidth": 4,
    "printWidth": 100
  }
```

[Read more about Prettier configuration options here.](https://prettier.io/docs/en/options.html)

#### Settings
Access extension settings within Visual Studio via Tools >>> Options, Prettier.

1. File Extensions to Format
    * Defaults to js,jsx,ts,tsx,json,css,scss,less,html,htm,md,markdown,yml,yaml
1. Format on Save
    * If true, run Prettier whenever a file of the configured type(s) is saved.
2. Prettier version for embedded usage: 
    * If your solution does not have a local version of Prettier installed via npm, the extension will attempt to download and use the version noted here.
    * Extension will download a requested version once and reuse that now embedded Prettier install until the setting requests another version.
    * If the version declared cannot be found via npm, the installation will fail. Check the output window for errors.

#### Can it use my bundled version of Prettier?
Yes, the plugin will search for a locally (relative to the open file) installed Prettier version before falling back to its own version. 

It does ***not*** currently support using a globally installed version of Prettier, and will use its embedded version instead.

#### Troubleshooting

If Prettier isn't formatting your files:

1. **Check the Output Window**: Go to `View > Output`, then select "Prettier x64" from the dropdown. All extension activity and errors are logged here.

## License
[Apache 2.0](LICENSE)
