cmake_minimum_required(VERSION 3.8)
project(dt_description)

find_package(ament_cmake REQUIRED)

install(DIRECTORY urdf launch rviz config
  DESTINATION share/${PROJECT_NAME}
)

# 공장 배치 표시물 (컨베이어·파레트 자리·팔·AGV 경로) → /factory_layout
install(PROGRAMS scripts/factory_layout_markers.py
  DESTINATION lib/${PROJECT_NAME}
)

ament_package()
