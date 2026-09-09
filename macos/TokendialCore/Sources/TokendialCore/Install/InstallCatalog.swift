import Foundation

/// The install recipes, read from the provider specs the app carries. A spec without an install block, like
/// GLM, has no recipe and its row simply never offers to install anything.
///
/// The directory is handed in rather than found, exactly as `Strings` takes its catalogue: the app passes the
/// bundle's copy of `docs/providers`, the tests pass the one in the repository.
public enum InstallCatalog {
    public private(set) static var all: [String: InstallRecipe] = [:]

    public static func load(from directory: URL) {
        let files = (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? []
        var found: [String: InstallRecipe] = [:]
        for file in files where file.pathExtension == "json" && file.lastPathComponent != "schema.json" {
            guard let data = try? Data(contentsOf: file), let root = JSON.object(data), let id = root.str("id"),
                  let recipe = parse(id, root) else { continue }
            found[id] = recipe
        }
        all = found
    }

    /// A profile shares the recipe of the tool it is an account of: claude-work installs Claude Code.
    public static func recipe(_ providerId: String) -> InstallRecipe? { all[ProviderFamily.of(providerId)] }

    private static func parse(_ id: String, _ root: JSONObject) -> InstallRecipe? {
        guard let install = root.obj("install"),
              let vendor = install.str("vendor"),
              let docsUrl = install.str("docsUrl"),
              let mac = install.obj("macos"),
              let kind = InstallKind(rawValue: mac.str("kind") ?? ""),
              let line = mac.str("install") else { return nil }
        let detect = mac.obj("detect") ?? [:]
        var step: SignInStep?
        if let signIn = mac.obj("signIn"), let command = signIn.str("command"), let hint = signIn.str("hint") {
            step = SignInStep(command: command, hint: hint)
        }
        return InstallRecipe(
            providerId: id,
            vendor: vendor,
            docsUrl: docsUrl,
            kind: kind,
            detect: Detect(commands: mac.obj("detect")?.arr("commands").compactMap { $0 as? String } ?? [],
                           paths: detect.arr("paths").compactMap { $0 as? String }),
            requires: mac.arr("requires").compactMap { $0 as? String },
            install: line,
            signIn: step,
            downloadUrl: mac.str("downloadUrl"))
    }
}
