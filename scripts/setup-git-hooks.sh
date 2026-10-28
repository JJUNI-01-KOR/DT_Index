#!/usr/bin/env bash
# 클론 후 1번만 실행: 버전 관리되는 .githooks/ 를 Git 훅 경로로 등록한다.
# Windows는 Git Bash, Ubuntu는 터미널에서 실행.

set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

chmod +x .githooks/* 2>/dev/null || true
git config core.hooksPath .githooks

# git-lfs 확인 (LFS 훅 4개는 .githooks/ 안에 이미 들어 있음)
if ! git lfs version >/dev/null 2>&1; then
  echo "⚠ git-lfs가 설치되어 있지 않습니다. 설치 후 'git lfs install'을 실행하세요." >&2
fi

echo "✔ Git 훅 활성화 완료 (core.hooksPath=.githooks)"

# Unity ROS Settings 파일: 각자 자기 VM IP를 적으므로 Git이 변경을 무시하게 한다 (브리지 방식)
ros_prefab="unity/INDEX_Twin/Assets/Resources/ROSConnectionPrefab.prefab"
if git ls-files --error-unmatch "$ros_prefab" >/dev/null 2>&1; then
  git update-index --skip-worktree "$ros_prefab"
  echo "✔ ROS 설정 파일 skip-worktree 적용: $ros_prefab"
  echo "  → Unity ROS Settings에 내 VM IP를 적어도 Git에 올라가지 않습니다."
else
  echo "ℹ ROS 설정 파일이 아직 저장소에 없습니다 ($ros_prefab)."
  echo "  D가 Unity 프로젝트를 커밋한 뒤 git pull → 이 스크립트를 한 번 더 실행하세요."
fi

echo ""
echo "확인: git config core.hooksPath   /   git ls-files -v | grep ROSConnectionPrefab  (S 로 시작하면 OK)"
