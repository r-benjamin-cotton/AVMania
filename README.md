# AVMania
Windows専用アマチュア向けシンプルビデオプレイヤーです。\
UnityのVideoPlayerでシークやタイムラインでの動作で不安定な事があったため代替として作成しました。\
以前AVProを高いお金出して買ったけれどすぐにdeprecatedになってしまいその悔しさをばねに。\
\
プロフェッショナルな方にはAVProのような高機能な物を使って頂きこちらの利用はご遠慮ください、\
AVManiaはアマチュアやインディ系や学生さんなどに気軽に使ってもらいたいです。\
といっても通常はUnity標準の機能で十分ですが。\


*Unity2022.3でのみ動作確認

*テストはあんまりできていない。

*ドキュメントあんまり書く気ない。

# Install
UnityのPackageManagerで"Add package from git URL..."にて以下を指定。\
https://github.com/r-benjamin-cotton/AVMania.git

# Sample
UnityのPackageManagerからImport。\
※サンプルをインポートすると自動的にテスト動画をStreamingAssetsへコピーします。

# Dependencies
Timeline

# Usage
AVPlayer: ビデオプレイヤー、StreamingAssetsからファイルを直接指定しての再生※VideoClipには非対応¥
トラック毎に出力先のRendererやRawImage、AVListenerやAVSourceを指定します。

AVRecorder: 簡易的なmp4録画

AVCapture: キャプチャーデバイス用、WebCamTextureやMicrophpneの代替

AVListener: 音声をAudioListenerへ流すためのコンポーネント

AVSource: 音声をAudioSourceへ流すためのコンポーネント

