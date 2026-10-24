---
title: Code signing policy — PC 健康診断
lang: ja
---

# Code signing policy（コード署名ポリシー）

## 現在の状態 / Status

- **2026-10-18 に SignPath Foundation の無料コード署名に申請しましたが、承認されませんでした。**理由は、このプロジェクトが公開されたばかりで、外部からの評価（利用者・記事など）がまだ十分でないためです。実績が付いたら再申請します。（Applied to SignPath Foundation on 2026-10-18; not approved because the project does not yet have enough public visibility. We plan to reapply later.）
- **現在配布している `PcReport.exe` はコード署名されていません。**Microsoft Store 版も、パッケージ（MSIX）は Microsoft が署名していますが、中の `PcReport.exe` そのものは未署名です。そのため、ウイルス対策ソフトが初回起動時に「不審なファイル」として確認を求めることがあります。
- 署名できるようになったら、下の手順でビルドした exe に署名してから配布します。

## 署名するもの / What is signed

- **`PcReport.exe` だけ**を署名します。
- exe は、このリポジトリのソースから **GitHub Actions**（GitHub がホストするランナー、`.github/workflows/build.yml`）でビルドしたものに限ります。手元でビルドした exe には署名しません。
- 署名の依頼は、毎回保守者が手動で承認します。
- 同じ commit からは同じ exe ができる（SHA256 が一致する）ことを確かめています。

## 役割 / Team roles

このプロジェクトは 1 人で保守しており、保守者がすべての役割を兼ねます。（This project has one maintainer, who holds all roles.）

- Committers and reviewers: [aokumi-33](https://github.com/aokumi-33)
- Approvers: [aokumi-33](https://github.com/aokumi-33)（[aokumi-apps](https://github.com/orgs/aokumi-apps/people?query=role%3Aowner) の Owner）

## プライバシー / Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

PC 健康診断は通信しません。集めた情報はこの PC の中だけで処理されます。詳しくは[プライバシーポリシー](privacy.html)をご覧ください。

## 問い合わせ / Contact

[33@aokumi.com](mailto:33@aokumi.com)
