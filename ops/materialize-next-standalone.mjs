import {
  copyFileSync,
  existsSync,
  lstatSync,
  mkdirSync,
  readlinkSync,
  readdirSync,
  realpathSync,
} from 'node:fs';
import path from 'node:path';

function fail(message) {
  throw new Error(message);
}

function isWithin(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative === '' ||
    (relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative));
}

const [sourceArgument, destinationArgument] = process.argv.slice(2);
if (!sourceArgument || !destinationArgument) {
  fail('usage: node materialize-next-standalone.mjs <source> <destination>');
}

const source = realpathSync(path.resolve(sourceArgument));
const destination = path.resolve(destinationArgument);
if (isWithin(source, destination)) {
  fail(`destination must not be inside source: ${destination}`);
}
if (existsSync(destination)) {
  fail(`destination already exists: ${destination}`);
}

let linkCount = 0;
const activeDirectories = new Set();

function getPackageContext(packagePath) {
  const parent = path.dirname(packagePath);
  if (path.basename(parent).toLowerCase() === 'node_modules') {
    return { root: parent, packageName: path.basename(packagePath) };
  }

  const grandparent = path.dirname(parent);
  if (path.basename(grandparent).toLowerCase() === 'node_modules' && path.basename(parent).startsWith('@')) {
    return {
      root: grandparent,
      packageName: path.join(path.basename(parent), path.basename(packagePath)),
    };
  }
  return null;
}

function listContextPackages(contextRoot) {
  const packages = [];
  for (const entry of readdirSync(contextRoot)) {
    const entryPath = path.join(contextRoot, entry);
    if (entry.startsWith('@') && lstatSync(entryPath).isDirectory()) {
      for (const scopedEntry of readdirSync(entryPath)) {
        packages.push({
          name: path.join(entry, scopedEntry),
          path: path.join(entryPath, scopedEntry),
        });
      }
    } else {
      packages.push({ name: entry, path: entryPath });
    }
  }
  return packages;
}

function copyMaterialized(sourcePath, destinationPath) {
  let effectiveSource = sourcePath;
  let metadata = lstatSync(effectiveSource);
  let packageContext = null;

  if (metadata.isSymbolicLink()) {
    // Do not stat/realpath the link itself. Next's Windows tracer can emit a
    // file-type symlink whose target is a directory; following that link fails
    // with EPERM even though its relative target is valid and readable.
    const rawTarget = readlinkSync(effectiveSource);
    effectiveSource = realpathSync(path.resolve(path.dirname(effectiveSource), rawTarget));
    if (!isWithin(source, effectiveSource)) {
      fail(`standalone link escapes its artifact root: ${sourcePath} -> ${effectiveSource}`);
    }
    metadata = lstatSync(effectiveSource);
    packageContext = getPackageContext(effectiveSource);
    linkCount += 1;
  }

  if (metadata.isDirectory()) {
    const directoryIdentity = realpathSync(effectiveSource);
    if (activeDirectories.has(directoryIdentity)) {
      fail(`standalone link cycle detected at: ${sourcePath}`);
    }
    activeDirectories.add(directoryIdentity);
    try {
      mkdirSync(destinationPath, { recursive: true });
      for (const entry of readdirSync(effectiveSource)) {
        if (path.basename(effectiveSource).toLowerCase() === 'node_modules' &&
            (entry === '.pnpm' || entry === '.bin')) {
          continue;
        }
        copyMaterialized(path.join(effectiveSource, entry), path.join(destinationPath, entry));
      }

      // A pnpm package normally resolves dependencies through symlinks beside
      // its real directory in the virtual store. After dereferencing the
      // package itself that realpath context is gone, so reproduce that exact
      // context as a private, physical node_modules under the copied package.
      if (packageContext) {
        for (const dependency of listContextPackages(packageContext.root)) {
          if (dependency.name.toLowerCase() === packageContext.packageName.toLowerCase()) continue;
          copyMaterialized(
            dependency.path,
            path.join(destinationPath, 'node_modules', dependency.name),
          );
        }
      }
    } finally {
      activeDirectories.delete(directoryIdentity);
    }
    return;
  }

  if (metadata.isFile()) {
    copyFileSync(effectiveSource, destinationPath);
    return;
  }

  fail(`unsupported entry in standalone artifact: ${sourcePath}`);
}

// pnpm-backed Next standalone output contains symlinks into its own .pnpm tree.
// Materialize them as ordinary files/directories so deployment never depends
// on the build workspace or on symlink privileges at YC.
copyMaterialized(source, destination);

console.log(`Materialized Next standalone artifact (${linkCount} links dereferenced): ${destination}`);
