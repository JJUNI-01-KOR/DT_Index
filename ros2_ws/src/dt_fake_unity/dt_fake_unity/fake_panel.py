"""fake_panel — 키보드로 Unity 제어 패널 버튼을 흉내 낸다. 담당 D · scope `fake`

Unity 제어 패널(5.4)이 생기기 전까지 B·C가 factory_manager를 시험할 때 쓴다.
토픽 이름은 SRS v2.3과 Unity TopicNames.cs를 따른다.

실행 (fake_unity_node와 다른 터미널에서)
    ros2 run dt_fake_unity fake_panel
"""
import time

import rclpy
from rclpy.node import Node
from std_msgs.msg import Bool, String

FAULT_TARGETS = ('arm_1', 'arm_2', 'arm_3', 'arm_4', 'conveyor', 'agv_1')

HELP = """\
 s = [동작]            /factory/start true
 e = [비상정지] 켬/끔   /factory/emergency_stop true ↔ false
 1 = [불량 팔레트 정리] /pallet_1_reset true
 2 = [출고 팔레트 정리] /pallet_2_reset true
 f = [고장 주입]        /factory/inject_fault <대상>
 c = [조치 완료]        /factory/fault_clear true
 h = 도움말   q = 끝"""


def main(args=None):
    rclpy.init(args=args)
    node = Node('fake_panel')
    start = node.create_publisher(Bool, '/factory/start', 10)
    estop = node.create_publisher(Bool, '/factory/emergency_stop', 10)
    reset_1 = node.create_publisher(Bool, '/pallet_1_reset', 10)
    reset_2 = node.create_publisher(Bool, '/pallet_2_reset', 10)
    fault = node.create_publisher(String, '/factory/inject_fault', 10)
    clear = node.create_publisher(Bool, '/factory/fault_clear', 10)
    log = node.get_logger()

    time.sleep(1.0)   # 막 만든 publisher는 첫 메시지를 놓칠 수 있어 연결될 시간을 준다
    stopped = False
    print(HELP)
    try:
        while rclpy.ok():
            key = input('> ').strip().lower()
            if key == 's':
                start.publish(Bool(data=True))
                log.info('[동작] /factory/start true')
            elif key == 'e':
                stopped = not stopped
                estop.publish(Bool(data=stopped))
                log.info(f'[비상정지] /factory/emergency_stop {str(stopped).lower()}'
                         f' ({"누름" if stopped else "해제"})')
            elif key == '1':
                reset_1.publish(Bool(data=True))
                log.info('[불량 팔레트 정리] /pallet_1_reset true')
            elif key == '2':
                reset_2.publish(Bool(data=True))
                log.info('[출고 팔레트 정리] /pallet_2_reset true')
            elif key == 'f':
                target = input(f'  대상 ({", ".join(FAULT_TARGETS)}): ').strip()
                if target not in FAULT_TARGETS:
                    print(f'  ✖ 대상은 {", ".join(FAULT_TARGETS)} 중 하나')
                    continue
                fault.publish(String(data=target))
                log.info(f'[고장 주입] /factory/inject_fault {target}')
            elif key == 'c':
                clear.publish(Bool(data=True))
                log.info('[조치 완료] /factory/fault_clear true')
            elif key in ('h', '?'):
                print(HELP)
            elif key == 'q':
                break
            elif key:
                print('  ? h를 누르면 도움말')
    except (KeyboardInterrupt, EOFError):
        pass
    finally:
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == '__main__':
    main()
