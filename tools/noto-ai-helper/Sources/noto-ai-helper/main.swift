// noto-ai-helper — on-device field suggestions for Noto, via Apple Intelligence.
//
// Contract with the app (Noto.Providers/Ai/AppleOnDeviceProvider.cs):
//   stdin  — one JSON object: {"system": "<instructions>", "user": "<raw title>"}
//   stdout — the model's reply, verbatim (the app parses it and tolerates code fences)
//   stderr — a human-readable reason, with a non-zero exit, when it cannot answer
//
// Everything runs in the SystemLanguageModel that ships with Apple Intelligence, so nothing leaves the
// Mac and there is no API key, endpoint or account involved.

import Foundation
import FoundationModels

struct Request: Decodable {
    let system: String
    let user: String
}

func fail(_ message: String) -> Never {
    FileHandle.standardError.write(Data((message + "\n").utf8))
    exit(1)
}

let input = FileHandle.standardInput.readDataToEndOfFile()
guard let request = try? JSONDecoder().decode(Request.self, from: input) else {
    fail("noto-ai-helper: expected {\"system\": …, \"user\": …} on stdin")
}

let model = SystemLanguageModel.default
guard model.isAvailable else {
    // The common causes: Apple Intelligence switched off, the model still downloading, or unsupported
    // hardware. `availability` carries the specific reason; surface it so Settings can explain itself.
    fail(
        "noto-ai-helper: Apple Intelligence isn't available on this Mac. "
            + "Turn it on in System Settings › Apple Intelligence & Siri, then try again."
    )
}

do {
    // Instructions are the system prompt; the user's raw title is the single turn. A fresh session per
    // call keeps one task's text out of the next one's context.
    let session = LanguageModelSession(instructions: request.system)
    let response = try await session.respond(to: request.user)
    print(response.content)
} catch {
    fail("noto-ai-helper: \(error.localizedDescription)")
}
