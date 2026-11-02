cmake_minimum_required(VERSION 3.8)
project(dt_bringup)

find_package(ament_cmake REQUIRED)

install(DIRECTORY launch config
  DESTINATION share/${PROJECT_NAME}
)

# ROS-TCP-Endpoint 스레드 여러 개 실행 (서비스 응답 기다리는 동안 토픽 전달이 멈추지 않게, 11/02 D)
install(PROGRAMS scripts/tcp_endpoint_mt.py
  DESTINATION lib/${PROJECT_NAME}
)

ament_package()
